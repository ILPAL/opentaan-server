package io.opentaan.admin.audit;

import java.time.Instant;
import java.util.UUID;

import io.opentaan.admin.security.Operator;

/**
 * One entry of the audit trail, as stored.
 *
 * @param sequence     position in the trail, from 1, without gaps
 * @param details      a JSON object, exactly as written; the hash covers this text
 * @param previousHash hash of the entry before, or 64 zeros for the first
 * @param entryHash    hash of this entry's fields and {@code previousHash}
 */
public record AuditEntry(
        long sequence,
        UUID entryId,
        Instant occurredAt,
        Operator actor,
        String action,
        String subjectType,
        String subjectId,
        String details,
        String previousHash,
        String entryHash) {
}
