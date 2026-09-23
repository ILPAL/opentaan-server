package io.opentaan.admin.outbox;

import java.time.Instant;
import java.util.UUID;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;

/** A row of {@code outbox_events}: something that happened and still has to be delivered. */
@Entity
@Table(name = "outbox_events")
class OutboxEvent {

    @Id
    @Column(name = "event_id")
    private UUID eventId;

    @Column(name = "aggregate_type", nullable = false)
    private String aggregateType;

    @Column(name = "aggregate_id", nullable = false)
    private String aggregateId;

    @Column(name = "event_type", nullable = false)
    private String eventType;

    @Column(name = "payload", nullable = false)
    private String payload;

    @Column(name = "created_at", nullable = false)
    private Instant createdAt;

    @Column(name = "published_at")
    private Instant publishedAt;

    protected OutboxEvent() {
    }

    OutboxEvent(String aggregateType, String aggregateId, String eventType, String payload, Instant createdAt) {
        this.eventId = UUID.randomUUID();
        this.aggregateType = aggregateType;
        this.aggregateId = aggregateId;
        this.eventType = eventType;
        this.payload = payload;
        this.createdAt = createdAt;
    }

    UUID getEventId() {
        return eventId;
    }

    String getEventType() {
        return eventType;
    }

    String getAggregateId() {
        return aggregateId;
    }

    String getPayload() {
        return payload;
    }

    Instant getPublishedAt() {
        return publishedAt;
    }
}
