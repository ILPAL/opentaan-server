package io.opentaan.admin.economy;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import java.util.List;
import java.util.Optional;
import java.util.concurrent.Callable;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;

import io.opentaan.admin.TestcontainersConfiguration;
import io.opentaan.admin.outbox.Outbox;
import io.opentaan.admin.security.Operator;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.jdbc.core.JdbcTemplate;
import org.testcontainers.junit.jupiter.Testcontainers;

/**
 * The adjustment workflow against a real PostgreSQL with the real migrations, so
 * the database constraints, the unique idempotency key and the outbox are all in play.
 */
@SpringBootTest(properties = {
        "spring.flyway.enabled=true",
        "spring.flyway.locations=filesystem:../database/migrations",
        "opentaan.economy.approval-threshold-gold=10000",
})
@Import(TestcontainersConfiguration.class)
@Testcontainers(disabledWithoutDocker = true)
class EconomyAdjustmentServiceIT {

    private static final Operator RIN = new Operator("sub-rin", "rin");
    private static final Operator AN = new Operator("sub-an", "an");
    private static final long CHARACTER = 2001L;

    @Autowired
    private EconomyAdjustmentService economy;

    @Autowired
    private Outbox outbox;

    @Autowired
    private JdbcTemplate jdbc;

    @BeforeEach
    void oneCharacter() {
        jdbc.update("DELETE FROM outbox_events");
        jdbc.update("DELETE FROM economy_adjustments");
        jdbc.update("DELETE FROM characters");
        jdbc.update("DELETE FROM accounts");
        jdbc.update("INSERT INTO accounts (account_id, email) VALUES (1001, 'player@example.com')");
        jdbc.update("""
                INSERT INTO characters (character_id, account_id, slot_index, name, name_key, info_json)
                VALUES (?, 1001, 0, 'Nova', 'nova', '{}')
                """, CHARACTER);
    }

    @Test
    void a_small_adjustment_is_approved_and_queued_for_the_game_server() {
        var outcome = economy.request(RIN, command(500), "key-small-0001");

        var adjustment = outcome.adjustment();
        assertThat(adjustment.status()).isEqualTo(AdjustmentStatus.APPROVED);
        assertThat(outbox.eventsFor(adjustment.adjustmentId().toString()))
                .singleElement()
                .satisfies(event -> {
                    assertThat(event.eventType()).isEqualTo("economy.adjustment.approved");
                    assertThat(event.payload()).contains("\"deltaGold\":500", "\"characterId\":" + CHARACTER);
                    assertThat(event.published()).isFalse();
                });
    }

    @Test
    void a_large_adjustment_waits_and_nothing_is_queued_until_approved() {
        var requested = economy.request(RIN, command(50_000), "key-large-0001").adjustment();
        var id = requested.adjustmentId();

        assertThat(requested.status()).isEqualTo(AdjustmentStatus.PENDING_APPROVAL);
        assertThat(outbox.eventsFor(id.toString())).isEmpty();

        assertThatThrownBy(() -> economy.approve(RIN, id, null)).isInstanceOf(SelfDecisionException.class);

        var approved = economy.approve(AN, id, "matches ticket 123");
        assertThat(approved.status()).isEqualTo(AdjustmentStatus.APPROVED);
        assertThat(approved.decidedBy()).isEqualTo(AN);
        assertThat(outbox.eventsFor(id.toString())).hasSize(1);

        // A retried approval changes nothing and queues nothing more.
        economy.approve(AN, id, "matches ticket 123");
        assertThat(outbox.eventsFor(id.toString())).hasSize(1);
    }

    @Test
    void a_rejected_adjustment_is_never_queued() {
        var id = economy.request(RIN, command(-75_000), "key-reject-001").adjustment().adjustmentId();

        var rejected = economy.reject(AN, id, "no ticket for this");

        assertThat(rejected.status()).isEqualTo(AdjustmentStatus.REJECTED);
        assertThat(outbox.eventsFor(id.toString())).isEmpty();
        assertThatThrownBy(() -> economy.approve(AN, id, null)).isInstanceOf(AdjustmentAlreadyDecidedException.class);
    }

    @Test
    void a_retry_with_the_same_key_returns_the_first_adjustment() {
        var first = economy.request(RIN, command(500), "key-retry-0001");
        var retry = economy.request(RIN, command(500), "key-retry-0001");

        assertThat(first.replayed()).isFalse();
        assertThat(retry.replayed()).isTrue();
        assertThat(retry.adjustment().adjustmentId()).isEqualTo(first.adjustment().adjustmentId());
        assertThat(rows()).isEqualTo(1);
        assertThat(outbox.eventsFor(first.adjustment().adjustmentId().toString())).hasSize(1);
    }

    @Test
    void the_same_key_with_a_different_amount_is_refused() {
        economy.request(RIN, command(500), "key-reuse-0001");

        assertThatThrownBy(() -> economy.request(RIN, command(600), "key-reuse-0001"))
                .isInstanceOf(IdempotencyKeyReusedException.class);
        assertThat(rows()).isEqualTo(1);
    }

    @Test
    void keys_belong_to_one_operator() {
        economy.request(RIN, command(500), "key-shared-001");
        var other = economy.request(AN, command(500), "key-shared-001");

        assertThat(other.replayed()).isFalse();
        assertThat(rows()).isEqualTo(2);
    }

    @Test
    void two_identical_requests_at_the_same_moment_create_one_adjustment() throws Exception {
        var start = new CountDownLatch(1);
        Callable<RequestOutcome> request = () -> {
            start.await();
            return economy.request(RIN, command(500), "key-race-00001");
        };
        try (var pool = Executors.newFixedThreadPool(2)) {
            List<Future<RequestOutcome>> results = List.of(pool.submit(request), pool.submit(request));
            start.countDown();

            var first = results.get(0).get();
            var second = results.get(1).get();
            assertThat(first.adjustment().adjustmentId()).isEqualTo(second.adjustment().adjustmentId());
        }
        assertThat(rows()).isEqualTo(1);
    }

    @Test
    void an_unknown_character_is_refused() {
        assertThatThrownBy(() -> economy.request(RIN, new AdjustmentRequestCommand(999, 500, "typo"), "key-unknown-01"))
                .isInstanceOf(CharacterNotFoundException.class);
    }

    @Test
    void the_list_filters_by_status_newest_first() {
        economy.request(RIN, command(500), "key-list-00001");
        economy.request(RIN, command(50_000), "key-list-00002");

        assertThat(economy.list(Optional.of(AdjustmentStatus.PENDING_APPROVAL), 50))
                .extracting(AdjustmentView::deltaGold)
                .containsExactly(50_000L);
        assertThat(economy.list(Optional.empty(), 50)).hasSize(2);
    }

    private static AdjustmentRequestCommand command(long deltaGold) {
        return new AdjustmentRequestCommand(CHARACTER, deltaGold, "refund for ticket 123");
    }

    private int rows() {
        return jdbc.queryForObject("SELECT count(*) FROM economy_adjustments", Integer.class);
    }
}
