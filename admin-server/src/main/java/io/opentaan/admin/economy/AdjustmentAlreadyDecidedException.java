package io.opentaan.admin.economy;

import java.util.UUID;

/** The adjustment is no longer waiting for a decision. */
public class AdjustmentAlreadyDecidedException extends RuntimeException {

    public AdjustmentAlreadyDecidedException(UUID adjustmentId, AdjustmentStatus status) {
        super("Adjustment " + adjustmentId + " is already " + status);
    }
}
