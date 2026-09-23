package io.opentaan.admin.economy;

/**
 * The result of requesting an adjustment.
 *
 * @param adjustment the adjustment, new or from an earlier identical request
 * @param replayed   {@code true} if an earlier request with the same idempotency key
 *                   and the same content created it
 */
public record RequestOutcome(AdjustmentView adjustment, boolean replayed) {
}
