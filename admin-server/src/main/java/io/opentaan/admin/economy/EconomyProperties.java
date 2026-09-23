package io.opentaan.admin.economy;

import org.springframework.boot.context.properties.ConfigurationProperties;

/**
 * Economy settings from {@code opentaan.economy}.
 *
 * @param approvalThresholdGold adjustments of at least this size, credit or debit,
 *                              need a second operator's approval
 */
@ConfigurationProperties("opentaan.economy")
public record EconomyProperties(long approvalThresholdGold) {

    public EconomyProperties {
        if (approvalThresholdGold < 1) {
            throw new IllegalArgumentException("opentaan.economy.approval-threshold-gold must be at least 1");
        }
    }

    boolean requiresApproval(long deltaGold) {
        return Math.abs(deltaGold) >= approvalThresholdGold;
    }
}
