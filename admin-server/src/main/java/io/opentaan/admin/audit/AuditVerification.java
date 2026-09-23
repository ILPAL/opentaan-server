package io.opentaan.admin.audit;

/**
 * The outcome of recomputing the audit trail's hash chain.
 *
 * @param intact         whether every entry fits the chain
 * @param entriesChecked how many entries were found to fit before the check ended
 * @param lastSequence   the last entry that fits, or {@code null} if none does
 * @param lastHash       that entry's hash
 * @param brokenAt       where the chain breaks, or {@code null} if it is intact
 * @param problem        what is wrong at {@code brokenAt}
 */
public record AuditVerification(
        boolean intact,
        long entriesChecked,
        Long lastSequence,
        String lastHash,
        Long brokenAt,
        String problem) {
}
