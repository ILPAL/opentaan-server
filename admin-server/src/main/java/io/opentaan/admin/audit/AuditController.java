package io.opentaan.admin.audit;

import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.util.List;
import java.util.Map;

import io.opentaan.admin.api.AuditApi;
import io.opentaan.admin.api.model.AuditTrailEntry;
import io.opentaan.admin.api.model.AuditTrailVerification;
import io.opentaan.admin.api.model.OperatorRef;
import io.opentaan.admin.security.Permissions;
import org.springframework.http.ResponseEntity;
import org.springframework.security.access.prepost.PreAuthorize;
import org.springframework.web.bind.annotation.RestController;
import tools.jackson.core.type.TypeReference;
import tools.jackson.databind.json.JsonMapper;

/** Implements the Audit operations of the OpenAPI contract. */
@RestController
class AuditController implements AuditApi {

    private static final TypeReference<Map<String, Object>> JSON_OBJECT = new TypeReference<>() {
    };

    private final AuditQueryService audit;
    private final JsonMapper json;

    AuditController(AuditQueryService audit, JsonMapper json) {
        this.audit = audit;
        this.json = json;
    }

    @Override
    @PreAuthorize("hasAuthority('" + Permissions.AUDIT_READ + "')")
    public ResponseEntity<List<AuditTrailEntry>> listAuditEntries(String subjectType, String subjectId,
            String actorId, Long before, Integer limit) {
        var entries = audit.list(new AuditFilter(subjectType, subjectId, actorId, before), limit == null ? 50 : limit);
        return ResponseEntity.ok(entries.stream().map(this::toApi).toList());
    }

    @Override
    @PreAuthorize("hasAuthority('" + Permissions.AUDIT_READ + "')")
    public ResponseEntity<AuditTrailVerification> verifyAuditTrail() {
        var result = audit.verify();
        return ResponseEntity.ok(new AuditTrailVerification(result.intact(), result.entriesChecked())
                .lastSequence(result.lastSequence())
                .lastHash(result.lastHash())
                .brokenAt(result.brokenAt())
                .problem(result.problem()));
    }

    private AuditTrailEntry toApi(AuditEntry entry) {
        return new AuditTrailEntry(entry.sequence(), entry.entryId(),
                OffsetDateTime.ofInstant(entry.occurredAt(), ZoneOffset.UTC),
                new OperatorRef(entry.actor().id(), entry.actor().name()), entry.action(), entry.subjectType(),
                entry.subjectId(), json.readValue(entry.details(), JSON_OBJECT), entry.previousHash(),
                entry.entryHash());
    }
}
