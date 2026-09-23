package io.opentaan.admin.economy;

import java.util.UUID;

/** The four-eyes rule: the operator who requested an adjustment cannot decide it. */
public class SelfDecisionException extends RuntimeException {

    public SelfDecisionException(UUID adjustmentId) {
        super("Adjustment " + adjustmentId + " was requested by you; another operator must decide it");
    }
}
