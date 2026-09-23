package io.opentaan.admin.economy;

import java.time.Instant;
import java.util.UUID;

import io.opentaan.admin.security.Operator;
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.EnumType;
import jakarta.persistence.Enumerated;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import jakarta.persistence.Version;

/**
 * A request to change a character's gold, and the decision on it.
 *
 * <p>The approval rules live here, next to the data they protect, and are repeated
 * as constraints in the database: an adjustment is decided at most once, never by
 * the operator who requested it, and a rejection always says why.
 */
@Entity
@Table(name = "economy_adjustments")
class EconomyAdjustment {

    @Id
    @Column(name = "adjustment_id")
    private UUID adjustmentId;

    @Column(name = "character_id", nullable = false)
    private long characterId;

    @Column(name = "delta_gold", nullable = false)
    private long deltaGold;

    @Column(name = "reason", nullable = false)
    private String reason;

    @Enumerated(EnumType.STRING)
    @Column(name = "status", nullable = false)
    private AdjustmentStatus status;

    @Column(name = "approval_required", nullable = false)
    private boolean approvalRequired;

    @Column(name = "requested_by_id", nullable = false)
    private String requestedById;

    @Column(name = "requested_by_name", nullable = false)
    private String requestedByName;

    @Column(name = "requested_at", nullable = false)
    private Instant requestedAt;

    @Column(name = "decided_by_id")
    private String decidedById;

    @Column(name = "decided_by_name")
    private String decidedByName;

    @Column(name = "decided_at")
    private Instant decidedAt;

    @Column(name = "decision_note")
    private String decisionNote;

    @Column(name = "idempotency_key", nullable = false)
    private String idempotencyKey;

    @Column(name = "request_hash", nullable = false)
    private String requestHash;

    @Version
    @Column(name = "version", nullable = false)
    private long version;

    protected EconomyAdjustment() {
    }

    /**
     * A new adjustment. Below the approval threshold it is approved on the spot;
     * at or above it, it waits for a second operator.
     */
    static EconomyAdjustment requested(AdjustmentRequestCommand request, Operator requester, boolean approvalRequired,
            Instant now, String idempotencyKey, String requestHash) {
        var adjustment = new EconomyAdjustment();
        adjustment.adjustmentId = UUID.randomUUID();
        adjustment.characterId = request.characterId();
        adjustment.deltaGold = request.deltaGold();
        adjustment.reason = request.reason();
        adjustment.approvalRequired = approvalRequired;
        adjustment.requestedById = requester.id();
        adjustment.requestedByName = requester.name();
        adjustment.requestedAt = now;
        adjustment.idempotencyKey = idempotencyKey;
        adjustment.requestHash = requestHash;
        if (approvalRequired) {
            adjustment.status = AdjustmentStatus.PENDING_APPROVAL;
        } else {
            adjustment.status = AdjustmentStatus.APPROVED;
            adjustment.decidedAt = now;
        }
        return adjustment;
    }

    /**
     * Approves the adjustment.
     *
     * @return {@code true} if this call approved it, {@code false} if the same
     *         operator had already approved it (a retry)
     */
    boolean approve(Operator approver, String note, Instant now) {
        return decide(AdjustmentStatus.APPROVED, approver, note, now);
    }

    /**
     * Rejects the adjustment.
     *
     * @return {@code true} if this call rejected it, {@code false} if the same
     *         operator had already rejected it (a retry)
     */
    boolean reject(Operator rejecter, String note, Instant now) {
        if (note == null || note.isBlank()) {
            throw new InvalidAdjustmentException("A rejection must say why");
        }
        return decide(AdjustmentStatus.REJECTED, rejecter, note, now);
    }

    private boolean decide(AdjustmentStatus decision, Operator decider, String note, Instant now) {
        if (status != AdjustmentStatus.PENDING_APPROVAL) {
            if (status == decision && decider.isSamePersonAs(decidedById)) {
                return false;
            }
            throw new AdjustmentAlreadyDecidedException(adjustmentId, status);
        }
        if (decider.isSamePersonAs(requestedById)) {
            throw new SelfDecisionException(adjustmentId);
        }
        status = decision;
        decidedById = decider.id();
        decidedByName = decider.name();
        decidedAt = now;
        decisionNote = note == null || note.isBlank() ? null : note.strip();
        return true;
    }

    boolean matchesRequest(String hash) {
        return requestHash.equals(hash);
    }

    AdjustmentView toView() {
        var decidedBy = decidedById == null ? null : new Operator(decidedById, decidedByName);
        return new AdjustmentView(adjustmentId, characterId, deltaGold, reason, status, approvalRequired,
                new Operator(requestedById, requestedByName), requestedAt, decidedBy, decidedAt, decisionNote);
    }

    UUID getAdjustmentId() {
        return adjustmentId;
    }

    long getCharacterId() {
        return characterId;
    }

    long getDeltaGold() {
        return deltaGold;
    }

    AdjustmentStatus getStatus() {
        return status;
    }

    Instant getDecidedAt() {
        return decidedAt;
    }
}
