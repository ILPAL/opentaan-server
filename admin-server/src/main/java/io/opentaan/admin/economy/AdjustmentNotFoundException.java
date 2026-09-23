package io.opentaan.admin.economy;

import java.util.UUID;

/** No adjustment has the requested id. */
public class AdjustmentNotFoundException extends RuntimeException {

    public AdjustmentNotFoundException(UUID adjustmentId) {
        super("No adjustment with id " + adjustmentId);
    }
}
