package io.opentaan.admin.economy;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import java.time.Instant;

import io.opentaan.admin.security.Operator;
import org.junit.jupiter.api.Test;

/** The approval rules, without a database or HTTP. */
class EconomyAdjustmentTest {

    private static final Operator REQUESTER = new Operator("op-requester", "rin");
    private static final Operator APPROVER = new Operator("op-approver", "an");
    private static final Operator THIRD = new Operator("op-third", "minh");
    private static final Instant NOW = Instant.parse("2026-09-22T10:00:00Z");

    @Test
    void below_the_threshold_it_is_approved_on_request_by_no_one() {
        var adjustment = requested(false);

        assertThat(adjustment.getStatus()).isEqualTo(AdjustmentStatus.APPROVED);
        assertThat(adjustment.toView().decidedBy()).isNull();
        assertThat(adjustment.toView().decidedAt()).isEqualTo(NOW);
    }

    @Test
    void at_the_threshold_it_waits_for_a_decision() {
        var adjustment = requested(true);

        assertThat(adjustment.getStatus()).isEqualTo(AdjustmentStatus.PENDING_APPROVAL);
        assertThat(adjustment.toView().decidedAt()).isNull();
    }

    @Test
    void the_requester_cannot_approve_their_own_adjustment() {
        var adjustment = requested(true);

        assertThatThrownBy(() -> adjustment.approve(REQUESTER, null, NOW))
                .isInstanceOf(SelfDecisionException.class);
        assertThat(adjustment.getStatus()).isEqualTo(AdjustmentStatus.PENDING_APPROVAL);
    }

    @Test
    void the_requester_cannot_reject_their_own_adjustment_either() {
        var adjustment = requested(true);

        assertThatThrownBy(() -> adjustment.reject(REQUESTER, "changed my mind", NOW))
                .isInstanceOf(SelfDecisionException.class);
    }

    @Test
    void another_operator_approves_it() {
        var adjustment = requested(true);

        assertThat(adjustment.approve(APPROVER, "  checked the ticket  ", NOW)).isTrue();

        var view = adjustment.toView();
        assertThat(view.status()).isEqualTo(AdjustmentStatus.APPROVED);
        assertThat(view.decidedBy()).isEqualTo(APPROVER);
        assertThat(view.decisionNote()).isEqualTo("checked the ticket");
    }

    @Test
    void approving_again_as_the_same_operator_is_a_harmless_retry() {
        var adjustment = requested(true);
        adjustment.approve(APPROVER, null, NOW);

        assertThat(adjustment.approve(APPROVER, null, NOW.plusSeconds(1))).isFalse();
        assertThat(adjustment.toView().decidedAt()).isEqualTo(NOW);
    }

    @Test
    void a_decided_adjustment_cannot_be_decided_differently() {
        var adjustment = requested(true);
        adjustment.approve(APPROVER, null, NOW);

        assertThatThrownBy(() -> adjustment.reject(THIRD, "too late", NOW))
                .isInstanceOf(AdjustmentAlreadyDecidedException.class);
        assertThatThrownBy(() -> adjustment.approve(THIRD, null, NOW))
                .isInstanceOf(AdjustmentAlreadyDecidedException.class);
    }

    @Test
    void a_rejection_must_say_why() {
        var adjustment = requested(true);

        assertThatThrownBy(() -> adjustment.reject(APPROVER, " ", NOW))
                .isInstanceOf(InvalidAdjustmentException.class);
        assertThat(adjustment.getStatus()).isEqualTo(AdjustmentStatus.PENDING_APPROVAL);
    }

    @Test
    void the_threshold_applies_to_debits_as_well_as_credits() {
        var properties = new EconomyProperties(10_000);

        assertThat(properties.requiresApproval(9_999)).isFalse();
        assertThat(properties.requiresApproval(10_000)).isTrue();
        assertThat(properties.requiresApproval(-10_000)).isTrue();
        assertThat(properties.requiresApproval(-9_999)).isFalse();
    }

    private static EconomyAdjustment requested(boolean approvalRequired) {
        return EconomyAdjustment.requested(new AdjustmentRequestCommand(7, 50_000, "refund for ticket 123"),
                REQUESTER, approvalRequired, NOW, "key-00000001", "hash");
    }
}
