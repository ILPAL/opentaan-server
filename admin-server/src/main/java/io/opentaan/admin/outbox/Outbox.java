package io.opentaan.admin.outbox;

import java.time.Clock;
import java.util.List;

import org.springframework.stereotype.Component;
import org.springframework.transaction.annotation.Propagation;
import org.springframework.transaction.annotation.Transactional;
import tools.jackson.databind.json.JsonMapper;

/**
 * Records events for delivery to other services, as part of the caller's transaction.
 *
 * <p>Writing the event in the same transaction as the change it describes means
 * the two commit or roll back together: an approved adjustment can never be lost
 * on its way to the game server, and a rolled-back one is never sent. Delivery is
 * a separate step that reads unpublished rows.
 */
@Component
public class Outbox {

    private final OutboxEventRepository events;
    private final JsonMapper json;
    private final Clock clock;

    Outbox(OutboxEventRepository events, JsonMapper json, Clock clock) {
        this.events = events;
        this.json = json;
        this.clock = clock;
    }

    /** Fails if there is no surrounding transaction, since the guarantee above would not hold. */
    @Transactional(propagation = Propagation.MANDATORY)
    public void record(String aggregateType, String aggregateId, String eventType, Object payload) {
        events.save(new OutboxEvent(
                aggregateType, aggregateId, eventType, json.writeValueAsString(payload), clock.instant()));
    }

    /** Events recorded for one aggregate, oldest first. */
    @Transactional(readOnly = true)
    public List<RecordedEvent> eventsFor(String aggregateId) {
        return events.findByAggregateIdOrderByCreatedAtAsc(aggregateId).stream()
                .map(e -> new RecordedEvent(e.getEventType(), e.getPayload(), e.getPublishedAt() != null))
                .toList();
    }

    /** An event as recorded, for inspection. */
    public record RecordedEvent(String eventType, String payload, boolean published) {
    }
}
