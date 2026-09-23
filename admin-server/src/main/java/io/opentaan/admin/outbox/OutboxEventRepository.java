package io.opentaan.admin.outbox;

import java.util.List;
import java.util.UUID;

import org.springframework.data.repository.Repository;

interface OutboxEventRepository extends Repository<OutboxEvent, UUID> {

    OutboxEvent save(OutboxEvent event);

    List<OutboxEvent> findByAggregateIdOrderByCreatedAtAsc(String aggregateId);
}
