package io.opentaan.admin.audit;

import static org.mockito.BDDMockito.given;
import static org.mockito.Mockito.verifyNoInteractions;
import static org.springframework.security.test.web.servlet.request.SecurityMockMvcRequestPostProcessors.jwt;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import java.time.Instant;
import java.util.List;
import java.util.Map;
import java.util.UUID;

import io.opentaan.admin.security.Operator;
import io.opentaan.admin.security.OperatorAuthoritiesConverter;
import io.opentaan.admin.security.OperatorRoles;
import io.opentaan.admin.security.SecurityConfig;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.webmvc.test.autoconfigure.WebMvcTest;
import org.springframework.context.annotation.Import;
import org.springframework.security.test.web.servlet.request.SecurityMockMvcRequestPostProcessors.JwtRequestPostProcessor;
import org.springframework.test.context.bean.override.mockito.MockitoBean;
import org.springframework.test.web.servlet.MockMvc;

/** The HTTP contract of the Audit operations: who may read the trail, filters, and the response shape. */
@WebMvcTest(AuditController.class)
@Import(SecurityConfig.class)
class AuditControllerTest {

    private static final String ENTRIES = "/api/audit/entries";
    private static final String VERIFICATION = "/api/audit/verification";
    private static final String ADJUSTMENT_ID = "5f8a3c1e-9d2b-4f6a-8c7e-1a2b3c4d5e6f";

    @Autowired
    private MockMvc mvc;

    @Autowired
    private OperatorRoles operatorRoles;

    @MockitoBean
    private AuditQueryService audit;

    @ParameterizedTest
    @ValueSource(strings = {"viewer", "economy", "support"})
    void reading_the_trail_needs_audit_read(String role) throws Exception {
        mvc.perform(get(ENTRIES).with(operator(role))).andExpect(status().isForbidden());
        mvc.perform(get(VERIFICATION).with(operator(role))).andExpect(status().isForbidden());
        verifyNoInteractions(audit);
    }

    @Test
    void an_auditor_lists_the_entries_about_one_adjustment() throws Exception {
        given(audit.list(AuditFilter.subject("economy-adjustment", ADJUSTMENT_ID), 50)).willReturn(List.of(
                new AuditEntry(7, UUID.fromString("0b9f4a52-3c1d-4e8f-9a6b-7c5d4e3f2a10"),
                        Instant.parse("2026-09-22T10:05:00.123456Z"), new Operator("sub-an", "an"),
                        AuditActions.ADJUSTMENT_APPROVED, "economy-adjustment", ADJUSTMENT_ID,
                        "{\"deltaGold\":50000,\"status\":\"APPROVED\"}", "a".repeat(64), "b".repeat(64))));

        mvc.perform(get(ENTRIES).param("subjectType", "economy-adjustment").param("subjectId", ADJUSTMENT_ID)
                        .with(operator("auditor")))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$[0].sequence").value(7))
                .andExpect(jsonPath("$[0].occurredAt").value("2026-09-22T10:05:00.123456Z"))
                .andExpect(jsonPath("$[0].actor.name").value("an"))
                .andExpect(jsonPath("$[0].action").value("economy.adjustment.approved"))
                .andExpect(jsonPath("$[0].details.deltaGold").value(50000))
                .andExpect(jsonPath("$[0].details.status").value("APPROVED"))
                .andExpect(jsonPath("$[0].previousHash").value("a".repeat(64)))
                .andExpect(jsonPath("$[0].entryHash").value("b".repeat(64)));
    }

    @Test
    void every_filter_reaches_the_service() throws Exception {
        given(audit.list(new AuditFilter("account", "42", "sub-an", 100L), 20)).willReturn(List.of());

        mvc.perform(get(ENTRIES).param("subjectType", "account").param("subjectId", "42")
                        .param("actorId", "sub-an").param("before", "100").param("limit", "20")
                        .with(operator("auditor")))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$").isEmpty());
    }

    @Test
    void filters_must_follow_the_contract() throws Exception {
        mvc.perform(get(ENTRIES).param("limit", "101").with(operator("auditor"))).andExpect(status().isBadRequest());
        mvc.perform(get(ENTRIES).param("before", "0").with(operator("auditor"))).andExpect(status().isBadRequest());
        mvc.perform(get(ENTRIES).param("subjectType", "Account;").with(operator("auditor")))
                .andExpect(status().isBadRequest());
        verifyNoInteractions(audit);
    }

    @Test
    void an_intact_trail_reports_its_last_entry() throws Exception {
        given(audit.verify()).willReturn(new AuditVerification(true, 42, 42L, "c".repeat(64), null, null));

        mvc.perform(get(VERIFICATION).with(operator("auditor")))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.intact").value(true))
                .andExpect(jsonPath("$.entriesChecked").value(42))
                .andExpect(jsonPath("$.lastSequence").value(42))
                .andExpect(jsonPath("$.lastHash").value("c".repeat(64)))
                .andExpect(jsonPath("$.brokenAt").doesNotExist());
    }

    @Test
    void a_broken_trail_says_where_and_why() throws Exception {
        given(audit.verify()).willReturn(new AuditVerification(false, 16, 16L, "d".repeat(64), 17L,
                "its hash does not match its contents"));

        mvc.perform(get(VERIFICATION).with(operator("auditor")))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.intact").value(false))
                .andExpect(jsonPath("$.brokenAt").value(17))
                .andExpect(jsonPath("$.problem").value("its hash does not match its contents"));
    }

    private JwtRequestPostProcessor operator(String... roles) {
        return jwt()
                .jwt(token -> token
                        .subject("sub-auditor")
                        .claim("preferred_username", "auditor")
                        .claim("realm_access", Map.of("roles", List.of(roles))))
                .authorities(new OperatorAuthoritiesConverter(operatorRoles));
    }
}
