package io.opentaan.admin.audit;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.concurrent.Callable;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;

import io.opentaan.admin.TestcontainersConfiguration;
import io.opentaan.admin.security.Operator;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.dao.DataIntegrityViolationException;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.transaction.IllegalTransactionStateException;
import org.springframework.transaction.PlatformTransactionManager;
import org.springframework.transaction.support.TransactionTemplate;
import org.testcontainers.junit.jupiter.Testcontainers;

/**
 * The audit trail against a real PostgreSQL with the real migrations: the database
 * links and hashes each entry, refuses changes, and the verifier here agrees with
 * it byte for byte.
 *
 * <p>Entries cannot be deleted, so tests never clean up. Each test looks only at
 * the entries it wrote, and tests that tamper with the trail roll back.
 */
@SpringBootTest(properties = {
        "spring.flyway.enabled=true",
        "spring.flyway.locations=filesystem:../database/migrations",
})
@Import(TestcontainersConfiguration.class)
@Testcontainers(disabledWithoutDocker = true)
class AuditTrailIT {

    private static final Operator RIN = new Operator("sub-rin", "rin");

    @Autowired
    private AuditTrail auditTrail;

    @Autowired
    private AuditQueryService audit;

    @Autowired
    private JdbcTemplate jdbc;

    @Autowired
    private PlatformTransactionManager transactionManager;

    @Test
    void the_database_numbers_links_and_hashes_each_entry() {
        var first = record("chain-1");
        var second = record("chain-2");
        var third = record("chain-3");

        assertThat(second.sequence()).isEqualTo(first.sequence() + 1);
        assertThat(third.sequence()).isEqualTo(second.sequence() + 1);
        assertThat(second.previousHash()).isEqualTo(first.entryHash());
        assertThat(third.previousHash()).isEqualTo(second.entryHash());
        // The hash the database stored is the one this code computes.
        assertThat(third.entryHash()).isEqualTo(AuditChain.hashOf(third));
        assertThat(audit.verify().intact()).isTrue();
    }

    @Test
    void an_entry_keeps_what_the_service_wrote() {
        var entry = record("kept-1");

        assertThat(entry.actor()).isEqualTo(RIN);
        assertThat(entry.action()).isEqualTo(AuditActions.ACCOUNT_VIEWED);
        assertThat(entry.subjectType()).isEqualTo("account");
        assertThat(entry.details()).isEqualTo("{\"note\":\"kept-1\"}");
    }

    @Test
    void the_database_refuses_to_change_or_remove_an_entry() {
        var entry = record("refused-1");

        assertThatThrownBy(() -> jdbc.update("UPDATE audit_entries SET actor_name = 'someone else' WHERE sequence = ?",
                entry.sequence()))
                .isInstanceOf(DataIntegrityViolationException.class)
                .hasMessageContaining("audit_entries is append-only: UPDATE is not allowed");
        assertThatThrownBy(() -> jdbc.update("DELETE FROM audit_entries WHERE sequence = ?", entry.sequence()))
                .isInstanceOf(DataIntegrityViolationException.class)
                .hasMessageContaining("audit_entries is append-only: DELETE is not allowed");
        assertThatThrownBy(() -> jdbc.execute("TRUNCATE audit_entries"))
                .isInstanceOf(DataIntegrityViolationException.class)
                .hasMessageContaining("audit_entries is append-only: TRUNCATE is not allowed");

        assertThat(only(entry.subjectId())).isEqualTo(entry);
    }

    @Test
    void the_database_chooses_the_place_in_the_chain() {
        var before = record("placed-1");

        inTransaction(() -> jdbc.update("""
                INSERT INTO audit_entries (entry_id, occurred_at, actor_id, actor_name, action, subject_type,
                                           subject_id, details, sequence, previous_hash, entry_hash)
                VALUES (gen_random_uuid(), now(), 'sub-rin', 'rin', 'account.viewed', 'account', 'placed-2', '{}',
                        1, repeat('0', 64), repeat('f', 64))
                """));

        var inserted = only("placed-2");
        assertThat(inserted.sequence()).isEqualTo(before.sequence() + 1);
        assertThat(inserted.previousHash()).isEqualTo(before.entryHash());
        assertThat(inserted.entryHash()).isEqualTo(AuditChain.hashOf(inserted));
    }

    @Test
    void recording_outside_a_transaction_is_refused() {
        assertThatThrownBy(() -> auditTrail.record(RIN, AuditActions.ACCOUNT_VIEWED, "account", "1", Map.of()))
                .isInstanceOf(IllegalTransactionStateException.class);
    }

    @Test
    void an_entry_rolls_back_with_its_transaction() {
        var transaction = new TransactionTemplate(transactionManager);
        transaction.executeWithoutResult(status -> {
            auditTrail.record(RIN, AuditActions.ACCOUNT_VIEWED, "account", "rolled-back-1", Map.of());
            status.setRollbackOnly();
        });

        assertThat(audit.list(AuditFilter.subject("account", "rolled-back-1"), 10)).isEmpty();
        assertThat(audit.verify().intact()).isTrue();
    }

    @Test
    void verification_finds_an_entry_changed_with_the_trigger_switched_off() {
        var entry = record("tampered-1");

        // Only the table's owner can switch the trigger off. DDL is transactional in
        // PostgreSQL, so rolling back restores both the trigger and the entry.
        var result = tamperThenVerify(
                "UPDATE audit_entries SET details = '{\"note\":\"forged\"}' WHERE sequence = " + entry.sequence());

        assertThat(result.intact()).isFalse();
        assertThat(result.brokenAt()).isEqualTo(entry.sequence());
        assertThat(result.problem()).isEqualTo("its hash does not match its contents");
        assertThat(audit.verify().intact()).isTrue();
    }

    @Test
    void verification_finds_a_removed_entry() {
        var entry = record("removed-1");
        record("removed-2");

        var result = tamperThenVerify("DELETE FROM audit_entries WHERE sequence = " + entry.sequence());

        assertThat(result.brokenAt()).isEqualTo(entry.sequence());
        assertThat(result.problem()).startsWith("entry " + entry.sequence() + " is missing");
    }

    @Test
    void concurrent_writers_form_one_unbroken_chain() throws Exception {
        var writers = 8;
        var entriesEach = 25;
        var start = new CountDownLatch(1);
        Callable<Void> writer = () -> {
            start.await();
            for (var i = 0; i < entriesEach; i++) {
                inTransaction(() -> auditTrail.record(RIN, AuditActions.ACCOUNT_VIEWED, "account", "concurrent-1",
                        Map.of()));
            }
            return null;
        };
        try (var pool = Executors.newFixedThreadPool(writers)) {
            var results = new ArrayList<Future<Void>>();
            for (var i = 0; i < writers; i++) {
                results.add(pool.submit(writer));
            }
            start.countDown();
            for (var result : results) {
                result.get();
            }
        }

        assertThat(jdbc.queryForObject("SELECT count(*) FROM audit_entries WHERE subject_id = 'concurrent-1'",
                Integer.class)).isEqualTo(writers * entriesEach);
        // Intact means numbered without gaps and linked without forks, from the first entry to the last.
        assertThat(audit.verify().intact()).isTrue();
    }

    @Test
    void the_list_pages_back_through_the_trail() {
        var first = record("paged-1");
        var second = record("paged-2");
        var third = record("paged-3");

        var newest = audit.list(new AuditFilter(null, null, RIN.id(), third.sequence() + 1), 2);
        var older = audit.list(new AuditFilter(null, null, RIN.id(), newest.getLast().sequence()), 1);

        assertThat(newest).containsExactly(third, second);
        assertThat(older).containsExactly(first);
    }

    /** Records one entry in its own transaction and returns it as stored. */
    private AuditEntry record(String subjectId) {
        inTransaction(() -> auditTrail.record(RIN, AuditActions.ACCOUNT_VIEWED, "account", subjectId,
                Map.of("note", subjectId)));
        return only(subjectId);
    }

    private AuditEntry only(String subjectId) {
        List<AuditEntry> entries = audit.list(AuditFilter.subject("account", subjectId), 10);
        assertThat(entries).hasSize(1);
        return entries.getFirst();
    }

    private AuditVerification tamperThenVerify(String tampering) {
        var transaction = new TransactionTemplate(transactionManager);
        return transaction.execute(status -> {
            jdbc.execute("ALTER TABLE audit_entries DISABLE TRIGGER audit_entries_append_only");
            jdbc.execute(tampering);
            var result = audit.verify();
            status.setRollbackOnly();
            return result;
        });
    }

    private void inTransaction(Runnable work) {
        new TransactionTemplate(transactionManager).executeWithoutResult(status -> work.run());
    }
}
