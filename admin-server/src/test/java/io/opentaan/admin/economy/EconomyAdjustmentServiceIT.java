package io.opentaan.admin.economy;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import java.util.ArrayList;
import java.util.List;
import java.util.Optional;
import java.util.UUID;
import java.util.concurrent.Callable;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;

import io.opentaan.admin.TestcontainersConfiguration;
import io.opentaan.admin.audit.AuditActions;
import io.opentaan.admin.audit.AuditEntry;
import io.opentaan.admin.audit.AuditFilter;
import io.opentaan.admin.audit.AuditQueryService;
import io.opentaan.admin.outbox.Outbox;
import io.opentaan.admin.security.Operator;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.orm.ObjectOptimisticLockingFailureException;
import org.testcontainers.junit.jupiter.Testcontainers;

/**
 * The adjustment workflow against a real PostgreSQL with the real migrations, so
 * the database constraints, the unique idempotency key, the outbox and the audit
 * trail are all in play.
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
    private static final Operator CHI = new Operator("sub-chi", "chi");
    private static final long CHARACTER = 2001L;

    @Autowired
    private EconomyAdjustmentService economy;

    @Autowired
    private Outbox outbox;

    @Autowired
    private AuditQueryService audit;

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
    void each_request_and_decision_is_audited_once() {
        var id = economy.request(RIN, command(50_000), "key-audit-0001").adjustment().adjustmentId();
        assertThatThrownBy(() -> economy.approve(RIN, id, null)).isInstanceOf(SelfDecisionException.class);
        economy.approve(AN, id, "matches ticket 123");
        economy.approve(AN, id, "matches ticket 123");

        // Newest first. The refused self-approval and the retried approval left nothing.
        var entries = auditOf(id);
        assertThat(entries).extracting(AuditEntry::action)
                .containsExactly(AuditActions.ADJUSTMENT_APPROVED, AuditActions.ADJUSTMENT_REQUESTED);
        assertThat(entries).extracting(AuditEntry::actor).containsExactly(AN, RIN);
        assertThat(entries.getFirst().details())
                .contains("\"status\":\"APPROVED\"", "\"decisionNote\":\"matches ticket 123\"");
        assertThat(entries.getLast().details()).contains("\"status\":\"PENDING_APPROVAL\"", "\"deltaGold\":50000");
    }

    @Test
    void a_rejection_is_audited_with_its_reason() {
        var id = economy.request(RIN, command(-75_000), "key-audit-0002").adjustment().adjustmentId();

        economy.reject(AN, id, "no ticket for this");
        economy.reject(AN, id, "no ticket for this");

        var entries = auditOf(id);
        assertThat(entries).extracting(AuditEntry::action)
                .containsExactly(AuditActions.ADJUSTMENT_REJECTED, AuditActions.ADJUSTMENT_REQUESTED);
        assertThat(entries.getFirst().details()).contains("\"decisionNote\":\"no ticket for this\"");
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
        assertThat(auditOf(first.adjustment().adjustmentId())).hasSize(1);
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
            assertThat(auditOf(first.adjustment().adjustmentId())).hasSize(1);
        }
        assertThat(rows()).isEqualTo(1);
    }

    @Test
    void of_two_operators_deciding_at_the_same_moment_exactly_one_wins() throws Exception {
        var id = economy.request(RIN, command(50_000), "key-decide-001").adjustment().adjustmentId();
        var start = new CountDownLatch(1);
        Callable<AdjustmentView> approve = () -> {
            start.await();
            return economy.approve(AN, id, "matches ticket 123");
        };
        Callable<AdjustmentView> reject = () -> {
            start.await();
            return economy.reject(CHI, id, "no ticket for this");
        };

        var outcomes = new ArrayList<Object>();
        try (var pool = Executors.newFixedThreadPool(2)) {
            var results = List.of(pool.submit(approve), pool.submit(reject));
            start.countDown();
            for (var result : results) {
                try {
                    outcomes.add(result.get());
                } catch (ExecutionException refused) {
                    outcomes.add(refused.getCause());
                }
            }
        }

        // Whichever decided second either lost the version check or saw the first
        // decision already committed; both answer 409.
        assertThat(outcomes).filteredOn(AdjustmentView.class::isInstance).hasSize(1);
        assertThat(outcomes).filteredOn(Throwable.class::isInstance).singleElement()
                .isInstanceOfAny(ObjectOptimisticLockingFailureException.class,
                        AdjustmentAlreadyDecidedException.class);
        assertThat(auditOf(id)).hasSize(2);
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

    private List<AuditEntry> auditOf(UUID adjustmentId) {
        return audit.list(AuditFilter.subject("economy-adjustment", adjustmentId.toString()), 10);
    }

    private int rows() {
        return jdbc.queryForObject("SELECT count(*) FROM economy_adjustments", Integer.class);
    }
}
