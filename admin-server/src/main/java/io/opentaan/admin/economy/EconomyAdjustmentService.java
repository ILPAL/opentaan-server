package io.opentaan.admin.economy;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.time.Clock;
import java.util.HexFormat;
import java.util.List;
import java.util.Optional;
import java.util.UUID;

import io.opentaan.admin.accounts.AccountQueryService;
import io.opentaan.admin.outbox.Outbox;
import io.opentaan.admin.security.Operator;
import org.springframework.dao.DataIntegrityViolationException;
import org.springframework.data.domain.Limit;
import org.springframework.stereotype.Service;
import org.springframework.transaction.PlatformTransactionManager;
import org.springframework.transaction.annotation.Transactional;
import org.springframework.transaction.support.TransactionTemplate;

/**
 * Requests and decides gold adjustments under dual control.
 *
 * <p>An approved adjustment is recorded in the outbox in the same transaction as
 * the approval; applying it to the character is the game server's job once the
 * event is delivered.
 */
@Service
public class EconomyAdjustmentService {

    private static final String AGGREGATE_TYPE = "economy-adjustment";

    private final EconomyAdjustmentRepository adjustments;
    private final AccountQueryService accounts;
    private final Outbox outbox;
    private final EconomyProperties properties;
    private final Clock clock;
    private final TransactionTemplate transaction;

    EconomyAdjustmentService(EconomyAdjustmentRepository adjustments, AccountQueryService accounts, Outbox outbox,
            EconomyProperties properties, Clock clock, PlatformTransactionManager transactionManager) {
        this.adjustments = adjustments;
        this.accounts = accounts;
        this.outbox = outbox;
        this.properties = properties;
        this.clock = clock;
        this.transaction = new TransactionTemplate(transactionManager);
    }

    /**
     * Requests an adjustment, or returns the one an earlier identical request with
     * the same idempotency key created.
     *
     * @throws InvalidAdjustmentException    for a zero amount or a blank reason
     * @throws CharacterNotFoundException    if the character does not exist
     * @throws IdempotencyKeyReusedException if the key was used for a different request
     */
    public RequestOutcome request(Operator requester, AdjustmentRequestCommand request, String idempotencyKey) {
        if (request.deltaGold() == 0) {
            throw new InvalidAdjustmentException("deltaGold must not be zero");
        }
        if (request.reason() == null || request.reason().isBlank()) {
            throw new InvalidAdjustmentException("An adjustment must say why");
        }
        var requestHash = hash(request);

        var earlier = adjustments.findByRequestedByIdAndIdempotencyKey(requester.id(), idempotencyKey);
        if (earlier.isPresent()) {
            return replay(earlier.get(), requestHash);
        }
        if (!accounts.characterExists(request.characterId())) {
            throw new CharacterNotFoundException(request.characterId());
        }

        try {
            var created = transaction.execute(status -> {
                var adjustment = EconomyAdjustment.requested(request, requester,
                        properties.requiresApproval(request.deltaGold()), clock.instant(), idempotencyKey,
                        requestHash);
                adjustments.saveAndFlush(adjustment);
                if (adjustment.getStatus() == AdjustmentStatus.APPROVED) {
                    recordApproval(adjustment);
                }
                return adjustment.toView();
            });
            return new RequestOutcome(created, false);
        } catch (DataIntegrityViolationException race) {
            // The same request arrived twice at once and the other one committed
            // first; the unique key on (requester, key) decided which. Answer with its result.
            var winner = adjustments.findByRequestedByIdAndIdempotencyKey(requester.id(), idempotencyKey)
                    .orElseThrow(() -> race);
            return replay(winner, requestHash);
        }
    }

    /**
     * Approves an adjustment waiting for approval.
     *
     * @throws SelfDecisionException              if the approver requested it
     * @throws AdjustmentAlreadyDecidedException  if it was already decided otherwise
     */
    @Transactional
    public AdjustmentView approve(Operator approver, UUID adjustmentId, String note) {
        var adjustment = find(adjustmentId);
        if (adjustment.approve(approver, note, clock.instant())) {
            recordApproval(adjustment);
        }
        return adjustment.toView();
    }

    /**
     * Rejects an adjustment waiting for approval. Nothing is sent to the game server.
     *
     * @throws SelfDecisionException              if the rejecter requested it
     * @throws AdjustmentAlreadyDecidedException  if it was already decided otherwise
     */
    @Transactional
    public AdjustmentView reject(Operator rejecter, UUID adjustmentId, String note) {
        var adjustment = find(adjustmentId);
        adjustment.reject(rejecter, note, clock.instant());
        return adjustment.toView();
    }

    @Transactional(readOnly = true)
    public AdjustmentView get(UUID adjustmentId) {
        return find(adjustmentId).toView();
    }

    /** Newest first, optionally only those in one status. */
    @Transactional(readOnly = true)
    public List<AdjustmentView> list(Optional<AdjustmentStatus> status, int limit) {
        var rows = status
                .map(s -> adjustments.findByStatusOrderByRequestedAtDesc(s, Limit.of(limit)))
                .orElseGet(() -> adjustments.findAllByOrderByRequestedAtDesc(Limit.of(limit)));
        return rows.stream().map(EconomyAdjustment::toView).toList();
    }

    private EconomyAdjustment find(UUID adjustmentId) {
        return adjustments.findById(adjustmentId).orElseThrow(() -> new AdjustmentNotFoundException(adjustmentId));
    }

    private void recordApproval(EconomyAdjustment adjustment) {
        outbox.record(AGGREGATE_TYPE, adjustment.getAdjustmentId().toString(), AdjustmentApproved.TYPE,
                new AdjustmentApproved(adjustment.getAdjustmentId(), adjustment.getCharacterId(),
                        adjustment.getDeltaGold(), adjustment.getDecidedAt()));
    }

    private static RequestOutcome replay(EconomyAdjustment earlier, String requestHash) {
        if (!earlier.matchesRequest(requestHash)) {
            throw new IdempotencyKeyReusedException();
        }
        return new RequestOutcome(earlier.toView(), true);
    }

    /** Identifies the content of a request, to tell a retry from a different request under the same key. */
    private static String hash(AdjustmentRequestCommand request) {
        var canonical = request.characterId() + "\n" + request.deltaGold() + "\n" + request.reason();
        try {
            var digest = MessageDigest.getInstance("SHA-256").digest(canonical.getBytes(StandardCharsets.UTF_8));
            return HexFormat.of().formatHex(digest);
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException("SHA-256 is always available", e);
        }
    }
}
