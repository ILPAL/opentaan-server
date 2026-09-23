package io.opentaan.admin.audit;

import static java.nio.charset.StandardCharsets.US_ASCII;
import static java.nio.charset.StandardCharsets.UTF_8;

import java.io.ByteArrayOutputStream;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.time.ZoneOffset;
import java.time.format.DateTimeFormatter;
import java.util.HexFormat;
import java.util.List;

/**
 * The hash chain over the audit trail, recomputed outside the database.
 *
 * <p>The database links and hashes each entry as it is inserted (see
 * {@code database/migrations/V3__audit_trail.sql}, which defines the canonical
 * form). This class implements the same definition independently, so checking
 * the chain does not depend on code stored in the database being checked.
 */
final class AuditChain {

    /** The previous hash of the first entry. */
    static final String GENESIS = "0".repeat(64);

    private static final DateTimeFormatter TIMESTAMP =
            DateTimeFormatter.ofPattern("uuuu-MM-dd'T'HH:mm:ss.SSSSSS'Z'").withZone(ZoneOffset.UTC);

    private AuditChain() {
    }

    /**
     * SHA-256 over the entry's fields and its previous hash, each written as a
     * netstring ({@code <length in UTF-8 bytes>:<value>,}) so that no text inside
     * a field can make two different entries hash the same.
     */
    static String hashOf(AuditEntry entry) {
        var fields = List.of(
                Long.toString(entry.sequence()),
                entry.entryId().toString(),
                TIMESTAMP.format(entry.occurredAt()),
                entry.actor().id(),
                entry.actor().name(),
                entry.action(),
                entry.subjectType(),
                entry.subjectId(),
                entry.details(),
                entry.previousHash());
        var canonical = new ByteArrayOutputStream();
        for (var field : fields) {
            var bytes = field.getBytes(UTF_8);
            canonical.writeBytes((bytes.length + ":").getBytes(US_ASCII));
            canonical.writeBytes(bytes);
            canonical.write(',');
        }
        return HexFormat.of().formatHex(sha256(canonical.toByteArray()));
    }

    private static byte[] sha256(byte[] input) {
        try {
            return MessageDigest.getInstance("SHA-256").digest(input);
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException("SHA-256 is always available", e);
        }
    }

    /**
     * Checks entries one at a time, in sequence order, and stops at the first one
     * that does not fit.
     */
    static final class Verifier {

        private long entriesChecked;
        private AuditEntry last;
        private Long brokenAt;
        private String problem;

        /** @return whether the chain is still intact, including this entry */
        boolean check(AuditEntry entry) {
            if (brokenAt != null) {
                return false;
            }
            var expectedSequence = last == null ? 1 : last.sequence() + 1;
            if (entry.sequence() != expectedSequence) {
                return broken(expectedSequence,
                        "entry " + expectedSequence + " is missing; the next entry is " + entry.sequence());
            }
            var expectedPrevious = last == null ? GENESIS : last.entryHash();
            if (!entry.previousHash().equals(expectedPrevious)) {
                return broken(entry.sequence(), last == null
                        ? "the first entry does not start the chain"
                        : "its previous hash is not the hash of entry " + last.sequence());
            }
            if (!entry.entryHash().equals(hashOf(entry))) {
                return broken(entry.sequence(), "its hash does not match its contents");
            }
            entriesChecked++;
            last = entry;
            return true;
        }

        AuditVerification result() {
            return new AuditVerification(brokenAt == null, entriesChecked,
                    last == null ? null : last.sequence(), last == null ? null : last.entryHash(),
                    brokenAt, problem);
        }

        private boolean broken(long sequence, String problem) {
            this.brokenAt = sequence;
            this.problem = problem;
            return false;
        }
    }
}
