package io.opentaan.admin.economy;

import java.time.Instant;
import java.util.UUID;

/** Outbox payload: an approved adjustment the game server has to apply. */
record AdjustmentApproved(UUID adjustmentId, long characterId, long deltaGold, Instant approvedAt) {

    static final String TYPE = "economy.adjustment.approved";
}
