package io.opentaan.admin.audit;

/**
 * Which audit entries to list. A {@code null} field matches every entry.
 *
 * @param before only entries with a lower sequence number, to page back through the trail
 */
public record AuditFilter(String subjectType, String subjectId, String actorId, Long before) {

    public static AuditFilter all() {
        return new AuditFilter(null, null, null, null);
    }

    public static AuditFilter subject(String subjectType, String subjectId) {
        return new AuditFilter(subjectType, subjectId, null, null);
    }
}
