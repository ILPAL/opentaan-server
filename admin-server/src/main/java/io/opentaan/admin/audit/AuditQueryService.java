package io.opentaan.admin.audit;

import java.sql.ResultSet;
import java.sql.SQLException;
import java.time.OffsetDateTime;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.UUID;

import io.opentaan.admin.security.Operator;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

/** Read side of the audit trail: listing entries and checking the chain. */
@Service
public class AuditQueryService {

    private static final String SELECT_ENTRIES = """
            SELECT sequence, entry_id, occurred_at, actor_id, actor_name, action,
                   subject_type, subject_id, details, previous_hash, entry_hash
            FROM audit_entries
            """;

    /** Entries read at a time while verifying, so a long trail never has to fit in memory. */
    private static final int VERIFY_BATCH_SIZE = 1_000;

    private final JdbcClient jdbc;

    AuditQueryService(JdbcClient jdbc) {
        this.jdbc = jdbc;
    }

    /** Entries matching the filter, newest first. */
    @Transactional(readOnly = true)
    public List<AuditEntry> list(AuditFilter filter, int limit) {
        var conditions = new ArrayList<String>();
        var params = new LinkedHashMap<String, Object>();
        if (filter.subjectType() != null) {
            conditions.add("subject_type = :subjectType");
            params.put("subjectType", filter.subjectType());
        }
        if (filter.subjectId() != null) {
            conditions.add("subject_id = :subjectId");
            params.put("subjectId", filter.subjectId());
        }
        if (filter.actorId() != null) {
            conditions.add("actor_id = :actorId");
            params.put("actorId", filter.actorId());
        }
        if (filter.before() != null) {
            conditions.add("sequence < :before");
            params.put("before", filter.before());
        }
        params.put("limit", limit);

        var where = conditions.isEmpty() ? "" : "WHERE " + String.join(" AND ", conditions) + "\n";
        return jdbc.sql(SELECT_ENTRIES + where + "ORDER BY sequence DESC LIMIT :limit")
                .params(params)
                .query(AuditQueryService::entry)
                .list();
    }

    /**
     * Recomputes the chain from the first entry to the last: no gaps in the
     * numbering, each entry naming the hash of the one before, and each hash
     * matching its entry. Stops at the first entry that does not fit.
     */
    @Transactional(readOnly = true)
    public AuditVerification verify() {
        var verifier = new AuditChain.Verifier();
        long after = 0;
        List<AuditEntry> batch;
        do {
            batch = jdbc.sql(SELECT_ENTRIES + "WHERE sequence > :after ORDER BY sequence LIMIT :limit")
                    .param("after", after)
                    .param("limit", VERIFY_BATCH_SIZE)
                    .query(AuditQueryService::entry)
                    .list();
            for (var entry : batch) {
                if (!verifier.check(entry)) {
                    return verifier.result();
                }
                after = entry.sequence();
            }
        } while (batch.size() == VERIFY_BATCH_SIZE);
        return verifier.result();
    }

    private static AuditEntry entry(ResultSet row, int rowNumber) throws SQLException {
        return new AuditEntry(
                row.getLong("sequence"),
                row.getObject("entry_id", UUID.class),
                row.getObject("occurred_at", OffsetDateTime.class).toInstant(),
                new Operator(row.getString("actor_id"), row.getString("actor_name")),
                row.getString("action"),
                row.getString("subject_type"),
                row.getString("subject_id"),
                row.getString("details"),
                row.getString("previous_hash"),
                row.getString("entry_hash"));
    }
}
