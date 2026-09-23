package io.opentaan.admin.economy;

import java.util.List;
import java.util.Optional;
import java.util.UUID;

import org.springframework.data.domain.Limit;
import org.springframework.data.repository.Repository;

/** Adjustments are created and decided, never deleted: there is no delete method. */
interface EconomyAdjustmentRepository extends Repository<EconomyAdjustment, UUID> {

    EconomyAdjustment saveAndFlush(EconomyAdjustment adjustment);

    Optional<EconomyAdjustment> findById(UUID adjustmentId);

    Optional<EconomyAdjustment> findByRequestedByIdAndIdempotencyKey(String requestedById, String idempotencyKey);

    List<EconomyAdjustment> findAllByOrderByRequestedAtDesc(Limit limit);

    List<EconomyAdjustment> findByStatusOrderByRequestedAtDesc(AdjustmentStatus status, Limit limit);
}
