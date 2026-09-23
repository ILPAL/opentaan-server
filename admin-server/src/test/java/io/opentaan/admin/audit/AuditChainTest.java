package io.opentaan.admin.audit;

import static org.assertj.core.api.Assertions.assertThat;

import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.UUID;

import io.opentaan.admin.security.Operator;
import org.junit.jupiter.api.Test;

/**
 * The chain as the verifier sees it. The database builds the chain and this code
 * checks it, so the two must agree on every byte of the canonical form.
 */
class AuditChainTest {

    private static final Operator RIN = new Operator("sub-rin", "rin");

    @Test
    void hashes_an_entry_exactly_as_the_database_does() {
        // Inserted into audit_entries on PostgreSQL 18 with the V3 migration; the
        // trigger stored this entry_hash. Non-ASCII text checks lengths in bytes.
        var entry = new AuditEntry(1, UUID.fromString("11111111-2222-3333-4444-555555555555"),
                Instant.parse("2026-09-22T10:00:00.123456Z"), RIN, "economy.adjustment.requested",
                "economy-adjustment", "abc", "{\"deltaGold\":50000,\"reason\":\"réfund ✓\"}", AuditChain.GENESIS,
                null);

        assertThat(AuditChain.hashOf(entry))
                .isEqualTo("c79ff506828d9247becc7488074d2cc866ced591187556b70b91e7b7fc359cf8");
    }

    @Test
    void text_cannot_move_between_fields() {
        // The same characters split differently between two fields: a plain
        // concatenation would hash both alike.
        var first = entry(1, AuditChain.GENESIS, new Operator("sub-rin", "rin"), "account.viewed");
        var second = entry(1, AuditChain.GENESIS, new Operator("sub-ri", "nrin"), "account.viewed");

        assertThat(AuditChain.hashOf(first)).isNotEqualTo(AuditChain.hashOf(second));
    }

    @Test
    void an_empty_trail_is_intact() {
        var result = new AuditChain.Verifier().result();

        assertThat(result).isEqualTo(new AuditVerification(true, 0, null, null, null, null));
    }

    @Test
    void an_untouched_chain_is_intact() {
        var chain = chain(3);

        var result = verify(chain);

        assertThat(result.intact()).isTrue();
        assertThat(result.entriesChecked()).isEqualTo(3);
        assertThat(result.lastSequence()).isEqualTo(3);
        assertThat(result.lastHash()).isEqualTo(chain.get(2).entryHash());
    }

    @Test
    void a_changed_entry_is_found() {
        var chain = chain(3);
        var original = chain.get(1);
        chain.set(1, new AuditEntry(original.sequence(), original.entryId(), original.occurredAt(),
                original.actor(), original.action(), original.subjectType(), original.subjectId(),
                "{\"deltaGold\":5}", original.previousHash(), original.entryHash()));

        var result = verify(chain);

        assertThat(result.intact()).isFalse();
        assertThat(result.brokenAt()).isEqualTo(2);
        assertThat(result.problem()).isEqualTo("its hash does not match its contents");
        assertThat(result.entriesChecked()).isEqualTo(1);
        assertThat(result.lastSequence()).isEqualTo(1);
    }

    @Test
    void a_changed_entry_with_its_hash_recomputed_breaks_the_next_link() {
        var chain = chain(3);
        var original = chain.get(1);
        chain.set(1, withHash(new AuditEntry(original.sequence(), original.entryId(), original.occurredAt(),
                original.actor(), original.action(), original.subjectType(), original.subjectId(),
                "{\"deltaGold\":5}", original.previousHash(), null)));

        var result = verify(chain);

        assertThat(result.brokenAt()).isEqualTo(3);
        assertThat(result.problem()).isEqualTo("its previous hash is not the hash of entry 2");
    }

    @Test
    void a_removed_entry_is_found() {
        var chain = chain(3);
        chain.remove(1);

        var result = verify(chain);

        assertThat(result.brokenAt()).isEqualTo(2);
        assertThat(result.problem()).isEqualTo("entry 2 is missing; the next entry is 3");
    }

    @Test
    void a_trail_must_start_from_the_genesis_hash() {
        var forged = withHash(entry(1, "f".repeat(64), RIN, "account.viewed"));

        var result = verify(List.of(forged));

        assertThat(result.brokenAt()).isEqualTo(1);
        assertThat(result.problem()).isEqualTo("the first entry does not start the chain");
    }

    @Test
    void checking_stops_at_the_first_break() {
        var chain = chain(4);
        chain.remove(1);
        var verifier = new AuditChain.Verifier();

        var fits = chain.stream().map(verifier::check).toList();

        assertThat(fits).containsExactly(true, false, false);
        assertThat(verifier.result().brokenAt()).isEqualTo(2);
    }

    /** A valid chain of {@code length} entries, linked and hashed as the database would. */
    private static List<AuditEntry> chain(int length) {
        var entries = new ArrayList<AuditEntry>();
        var previousHash = AuditChain.GENESIS;
        for (var sequence = 1; sequence <= length; sequence++) {
            var entry = withHash(entry(sequence, previousHash, RIN, "economy.adjustment.requested"));
            entries.add(entry);
            previousHash = entry.entryHash();
        }
        return entries;
    }

    private static AuditEntry entry(long sequence, String previousHash, Operator actor, String action) {
        return new AuditEntry(sequence, UUID.nameUUIDFromBytes(Long.toString(sequence).getBytes()),
                Instant.parse("2026-09-22T10:00:00Z").plusSeconds(sequence), actor, action, "economy-adjustment",
                "adj-" + sequence, "{\"deltaGold\":50000}", previousHash, null);
    }

    private static AuditEntry withHash(AuditEntry entry) {
        return new AuditEntry(entry.sequence(), entry.entryId(), entry.occurredAt(), entry.actor(), entry.action(),
                entry.subjectType(), entry.subjectId(), entry.details(), entry.previousHash(),
                AuditChain.hashOf(entry));
    }

    private static AuditVerification verify(List<AuditEntry> entries) {
        var verifier = new AuditChain.Verifier();
        entries.forEach(verifier::check);
        return verifier.result();
    }
}
