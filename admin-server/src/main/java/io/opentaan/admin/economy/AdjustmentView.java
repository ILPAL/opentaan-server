package io.opentaan.admin.economy;

import java.time.Instant;
import java.util.UUID;

import io.opentaan.admin.security.Operator;

/**
 * An adjustment as the rest of the application sees it, independent of both the
 * database mapping and the API model.
 */
public record AdjustmentView(
        UUID adjustmentId,
        long characterId,
        long deltaGold,
        String reason,
        AdjustmentStatus status,
        boolean approvalRequired,
        Operator requestedBy,
        Instant requestedAt,
        Operator decidedBy,
        Instant decidedAt,
        String decisionNote) {
}
