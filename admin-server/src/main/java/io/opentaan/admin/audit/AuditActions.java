package io.opentaan.admin.audit;

/**
 * Every action the audit trail records: each change an operator makes, and each
 * read of a player's personal data. Named {@code <subject>.<what happened>}.
 */
public final class AuditActions {

    public static final String ACCOUNT_VIEWED = "account.viewed";
    public static final String ADJUSTMENT_REQUESTED = "economy.adjustment.requested";
    public static final String ADJUSTMENT_APPROVED = "economy.adjustment.approved";
    public static final String ADJUSTMENT_REJECTED = "economy.adjustment.rejected";

    private AuditActions() {
    }
}
