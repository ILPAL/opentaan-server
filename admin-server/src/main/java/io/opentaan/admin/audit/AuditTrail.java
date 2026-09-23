package io.opentaan.admin.audit;

import java.time.Clock;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.time.temporal.ChronoUnit;
import java.util.UUID;

import io.opentaan.admin.security.Operator;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.stereotype.Component;
import org.springframework.transaction.annotation.Propagation;
import org.springframework.transaction.annotation.Transactional;
import tools.jackson.databind.json.JsonMapper;

/**
 * Appends entries to the audit trail, as part of the caller's transaction.
 *
 * <p>An entry commits or rolls back with the action it records, so the trail never
 * shows an action that did not happen and never misses one that did. The database
 * gives each entry its place in the chain and its hash; this class only says what
 * happened.
 *
 * <p>Appending locks the end of the chain until the transaction ends. Make it the
 * last write of a transaction, so the lock is held for as short a time as possible.
 */
@Component
public class AuditTrail {

    private final JdbcClient jdbc;
    private final JsonMapper json;
    private final Clock clock;

    AuditTrail(JdbcClient jdbc, JsonMapper json, Clock clock) {
        this.jdbc = jdbc;
        this.json = json;
        this.clock = clock;
    }

    /**
     * Records that {@code actor} did {@code action} to the given subject.
     *
     * <p>Fails if there is no surrounding transaction, since the entry could then
     * outlive a rolled-back action.
     *
     * @param details the facts of the action, written as a JSON object
     */
    @Transactional(propagation = Propagation.MANDATORY)
    public void record(Operator actor, String action, String subjectType, String subjectId, Object details) {
        // The database keeps microseconds; truncating here means the stored time is
        // exactly the time recorded, not a rounding of it.
        var occurredAt = OffsetDateTime.ofInstant(clock.instant().truncatedTo(ChronoUnit.MICROS), ZoneOffset.UTC);
        jdbc.sql("""
                        INSERT INTO audit_entries
                            (entry_id, occurred_at, actor_id, actor_name, action, subject_type, subject_id, details)
                        VALUES
                            (:entryId, :occurredAt, :actorId, :actorName, :action, :subjectType, :subjectId, :details)
                        """)
                .param("entryId", UUID.randomUUID())
                .param("occurredAt", occurredAt)
                .param("actorId", actor.id())
                .param("actorName", actor.name())
                .param("action", action)
                .param("subjectType", subjectType)
                .param("subjectId", subjectId)
                .param("details", json.writeValueAsString(details))
                .update();
    }
}
