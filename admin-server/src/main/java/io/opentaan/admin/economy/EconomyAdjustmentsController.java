package io.opentaan.admin.economy;

import java.net.URI;
import java.time.Instant;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.util.List;
import java.util.Optional;
import java.util.UUID;

import io.opentaan.admin.api.EconomyApi;
import io.opentaan.admin.api.model.Adjustment;
import io.opentaan.admin.api.model.AdjustmentRequest;
import io.opentaan.admin.api.model.ApprovalRequest;
import io.opentaan.admin.api.model.OperatorRef;
import io.opentaan.admin.api.model.RejectionRequest;
import io.opentaan.admin.security.CurrentOperator;
import io.opentaan.admin.security.Operator;
import io.opentaan.admin.security.Permissions;
import org.springframework.http.ResponseEntity;
import org.springframework.security.access.prepost.PreAuthorize;
import org.springframework.web.bind.annotation.RestController;

/** Implements the Economy operations of the OpenAPI contract. */
@RestController
class EconomyAdjustmentsController implements EconomyApi {

    private static final String REPLAYED_HEADER = "Idempotent-Replayed";

    private final EconomyAdjustmentService economy;
    private final CurrentOperator currentOperator;

    EconomyAdjustmentsController(EconomyAdjustmentService economy, CurrentOperator currentOperator) {
        this.economy = economy;
        this.currentOperator = currentOperator;
    }

    @Override
    @PreAuthorize("hasAuthority('" + Permissions.ECONOMY_ADJUST + "')")
    public ResponseEntity<Adjustment> requestAdjustment(String idempotencyKey, AdjustmentRequest body) {
        var outcome = economy.request(currentOperator.get(),
                new AdjustmentRequestCommand(body.getCharacterId(), body.getDeltaGold(), body.getReason()),
                idempotencyKey);
        var adjustment = outcome.adjustment();
        var response = ResponseEntity.created(URI.create("/api/economy/adjustments/" + adjustment.adjustmentId()));
        if (outcome.replayed()) {
            response.header(REPLAYED_HEADER, "true");
        }
        return response.body(toApi(adjustment));
    }

    @Override
    @PreAuthorize("hasAuthority('" + Permissions.LEDGER_READ + "')")
    public ResponseEntity<List<Adjustment>> listAdjustments(
            io.opentaan.admin.api.model.AdjustmentStatus status, Integer limit) {
        var filter = Optional.ofNullable(status).map(s -> AdjustmentStatus.valueOf(s.name()));
        var adjustments = economy.list(filter, limit == null ? 50 : limit);
        return ResponseEntity.ok(adjustments.stream().map(EconomyAdjustmentsController::toApi).toList());
    }

    @Override
    @PreAuthorize("hasAuthority('" + Permissions.LEDGER_READ + "')")
    public ResponseEntity<Adjustment> getAdjustment(UUID adjustmentId) {
        return ResponseEntity.ok(toApi(economy.get(adjustmentId)));
    }

    @Override
    @PreAuthorize("hasAuthority('" + Permissions.ECONOMY_ADJUST + "')")
    public ResponseEntity<Adjustment> approveAdjustment(UUID adjustmentId, ApprovalRequest body) {
        var note = body == null ? null : body.getNote();
        return ResponseEntity.ok(toApi(economy.approve(currentOperator.get(), adjustmentId, note)));
    }

    @Override
    @PreAuthorize("hasAuthority('" + Permissions.ECONOMY_ADJUST + "')")
    public ResponseEntity<Adjustment> rejectAdjustment(UUID adjustmentId, RejectionRequest body) {
        return ResponseEntity.ok(toApi(economy.reject(currentOperator.get(), adjustmentId, body.getNote())));
    }

    private static Adjustment toApi(AdjustmentView view) {
        var adjustment = new Adjustment(view.adjustmentId(), view.characterId(), view.deltaGold(), view.reason(),
                io.opentaan.admin.api.model.AdjustmentStatus.valueOf(view.status().name()),
                view.approvalRequired(), toApi(view.requestedBy()), toApi(view.requestedAt()));
        if (view.decidedBy() != null) {
            adjustment.decidedBy(toApi(view.decidedBy()));
        }
        if (view.decidedAt() != null) {
            adjustment.decidedAt(toApi(view.decidedAt()));
        }
        return adjustment.decisionNote(view.decisionNote());
    }

    private static OperatorRef toApi(Operator operator) {
        return new OperatorRef(operator.id(), operator.name());
    }

    private static OffsetDateTime toApi(Instant instant) {
        return OffsetDateTime.ofInstant(instant, ZoneOffset.UTC);
    }
}
