package io.opentaan.admin.economy;

import static org.hamcrest.Matchers.endsWith;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.ArgumentMatchers.anyInt;
import static org.mockito.ArgumentMatchers.eq;
import static org.mockito.BDDMockito.given;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.verifyNoInteractions;
import static org.springframework.security.test.web.servlet.request.SecurityMockMvcRequestPostProcessors.jwt;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.header;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import java.time.Instant;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.UUID;

import io.opentaan.admin.security.CurrentOperator;
import io.opentaan.admin.security.Operator;
import io.opentaan.admin.security.OperatorAuthoritiesConverter;
import io.opentaan.admin.security.OperatorRoles;
import io.opentaan.admin.security.SecurityConfig;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.webmvc.test.autoconfigure.WebMvcTest;
import org.springframework.context.annotation.Import;
import org.springframework.http.MediaType;
import org.springframework.security.test.web.servlet.request.SecurityMockMvcRequestPostProcessors.JwtRequestPostProcessor;
import org.springframework.test.context.bean.override.mockito.MockitoBean;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.test.web.servlet.request.MockHttpServletRequestBuilder;

/**
 * The HTTP contract of the Economy operations: who may call what, which header
 * and body rules apply, and how each outcome of the service is answered.
 */
@WebMvcTest(EconomyAdjustmentsController.class)
@Import({SecurityConfig.class, CurrentOperator.class})
class EconomyAdjustmentsControllerTest {

    private static final String ADJUSTMENTS = "/api/economy/adjustments";
    private static final UUID ID = UUID.fromString("5f8a3c1e-9d2b-4f6a-8c7e-1a2b3c4d5e6f");
    private static final String KEY = "form-7c1e2d";
    private static final Operator RIN = new Operator("sub-rin", "rin");
    private static final Operator AN = new Operator("sub-an", "an");

    @Autowired
    private MockMvc mvc;

    @Autowired
    private OperatorRoles operatorRoles;

    @MockitoBean
    private EconomyAdjustmentService economy;

    // ---- request ----

    @Test
    void requesting_needs_economy_adjust() throws Exception {
        mvc.perform(newAdjustment(50_000).with(operator(RIN, "viewer"))).andExpect(status().isForbidden());
        verifyNoInteractions(economy);
    }

    @Test
    void a_new_request_answers_201_with_its_location() throws Exception {
        given(economy.request(eq(RIN), any(), eq(KEY))).willReturn(new RequestOutcome(pending(), false));

        mvc.perform(newAdjustment(50_000).with(operator(RIN, "economy")))
                .andExpect(status().isCreated())
                .andExpect(header().string("Location", endsWith("/api/economy/adjustments/" + ID)))
                .andExpect(header().doesNotExist("Idempotent-Replayed"))
                .andExpect(jsonPath("$.status").value("PENDING_APPROVAL"))
                .andExpect(jsonPath("$.approvalRequired").value(true))
                .andExpect(jsonPath("$.requestedBy.name").value("rin"));

        verify(economy).request(RIN, new AdjustmentRequestCommand(7, 50_000, "refund for ticket 123"), KEY);
    }

    @Test
    void a_retry_is_marked_as_replayed() throws Exception {
        given(economy.request(eq(RIN), any(), eq(KEY))).willReturn(new RequestOutcome(pending(), true));

        mvc.perform(newAdjustment(50_000).with(operator(RIN, "economy")))
                .andExpect(status().isCreated())
                .andExpect(header().string("Idempotent-Replayed", "true"));
    }

    @Test
    void the_idempotency_key_is_required() throws Exception {
        mvc.perform(post(ADJUSTMENTS).contentType(MediaType.APPLICATION_JSON).content(body(50_000))
                        .with(operator(RIN, "economy")))
                .andExpect(status().isBadRequest());
        verifyNoInteractions(economy);
    }

    @Test
    void the_idempotency_key_must_follow_the_contract() throws Exception {
        mvc.perform(post(ADJUSTMENTS).header("Idempotency-Key", "bad key!").contentType(MediaType.APPLICATION_JSON)
                        .content(body(50_000)).with(operator(RIN, "economy")))
                .andExpect(status().isBadRequest());
        verifyNoInteractions(economy);
    }

    @Test
    void a_reason_is_required() throws Exception {
        mvc.perform(post(ADJUSTMENTS).header("Idempotency-Key", KEY).contentType(MediaType.APPLICATION_JSON)
                        .content("{\"characterId\": 7, \"deltaGold\": 500}").with(operator(RIN, "economy")))
                .andExpect(status().isBadRequest());
        verifyNoInteractions(economy);
    }

    @Test
    void a_zero_amount_is_a_bad_request() throws Exception {
        given(economy.request(any(), any(), any())).willThrow(new InvalidAdjustmentException("deltaGold must not be zero"));

        mvc.perform(newAdjustment(0).with(operator(RIN, "economy")))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.title").value("Invalid adjustment"));
    }

    @Test
    void a_reused_key_with_a_different_body_is_a_conflict() throws Exception {
        given(economy.request(any(), any(), any())).willThrow(new IdempotencyKeyReusedException());

        mvc.perform(newAdjustment(50_000).with(operator(RIN, "economy")))
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.title").value("Idempotency key reused"));
    }

    @Test
    void an_unknown_character_is_not_found() throws Exception {
        given(economy.request(any(), any(), any())).willThrow(new CharacterNotFoundException(7));

        mvc.perform(newAdjustment(50_000).with(operator(RIN, "economy")))
                .andExpect(status().isNotFound())
                .andExpect(jsonPath("$.title").value("Character not found"));
    }

    // ---- decide ----

    @Test
    void another_operator_approves() throws Exception {
        given(economy.approve(AN, ID, "checked")).willReturn(approvedBy(AN));

        mvc.perform(post(ADJUSTMENTS + "/{id}/approve", ID).contentType(MediaType.APPLICATION_JSON)
                        .content("{\"note\": \"checked\"}").with(operator(AN, "economy")))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.status").value("APPROVED"))
                .andExpect(jsonPath("$.decidedBy.name").value("an"));
    }

    @Test
    void approving_without_a_body_is_allowed() throws Exception {
        given(economy.approve(AN, ID, null)).willReturn(approvedBy(AN));

        mvc.perform(post(ADJUSTMENTS + "/{id}/approve", ID).with(operator(AN, "economy")))
                .andExpect(status().isOk());
    }

    @Test
    void the_requester_is_refused_with_403() throws Exception {
        given(economy.approve(RIN, ID, null)).willThrow(new SelfDecisionException(ID));

        mvc.perform(post(ADJUSTMENTS + "/{id}/approve", ID).with(operator(RIN, "economy")))
                .andExpect(status().isForbidden())
                .andExpect(jsonPath("$.title").value("Another operator must decide this adjustment"));
    }

    @Test
    void deciding_twice_differently_is_a_conflict() throws Exception {
        given(economy.reject(AN, ID, "no ticket")).willThrow(
                new AdjustmentAlreadyDecidedException(ID, AdjustmentStatus.APPROVED));

        mvc.perform(post(ADJUSTMENTS + "/{id}/reject", ID).contentType(MediaType.APPLICATION_JSON)
                        .content("{\"note\": \"no ticket\"}").with(operator(AN, "economy")))
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.title").value("Adjustment already decided"));
    }

    @Test
    void a_rejection_needs_a_note() throws Exception {
        mvc.perform(post(ADJUSTMENTS + "/{id}/reject", ID).contentType(MediaType.APPLICATION_JSON)
                        .content("{}").with(operator(AN, "economy")))
                .andExpect(status().isBadRequest());
        verifyNoInteractions(economy);
    }

    @Test
    void deciding_needs_economy_adjust() throws Exception {
        mvc.perform(post(ADJUSTMENTS + "/{id}/approve", ID).with(operator(AN, "auditor")))
                .andExpect(status().isForbidden());
        verifyNoInteractions(economy);
    }

    // ---- read ----

    @Test
    void an_auditor_can_list_adjustments_waiting_for_approval() throws Exception {
        given(economy.list(Optional.of(AdjustmentStatus.PENDING_APPROVAL), 20)).willReturn(List.of(pending()));

        mvc.perform(get(ADJUSTMENTS).param("status", "PENDING_APPROVAL").param("limit", "20")
                        .with(operator(AN, "auditor")))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$[0].adjustmentId").value(ID.toString()));
    }

    @Test
    void reading_needs_ledger_read() throws Exception {
        mvc.perform(get(ADJUSTMENTS).with(operator(AN, "operations"))).andExpect(status().isForbidden());
        mvc.perform(get(ADJUSTMENTS + "/{id}", ID).with(operator(AN, "operations"))).andExpect(status().isForbidden());
        verifyNoInteractions(economy);
    }

    @Test
    void a_limit_above_100_is_refused() throws Exception {
        mvc.perform(get(ADJUSTMENTS).param("limit", "101").with(operator(AN, "auditor")))
                .andExpect(status().isBadRequest());
        verify(economy, org.mockito.Mockito.never()).list(any(), anyInt());
    }

    @Test
    void an_unknown_adjustment_is_not_found() throws Exception {
        given(economy.get(ID)).willThrow(new AdjustmentNotFoundException(ID));

        mvc.perform(get(ADJUSTMENTS + "/{id}", ID).with(operator(AN, "auditor")))
                .andExpect(status().isNotFound())
                .andExpect(jsonPath("$.title").value("Adjustment not found"));
    }

    // ---- helpers ----

    private static MockHttpServletRequestBuilder newAdjustment(long deltaGold) {
        return post(ADJUSTMENTS).header("Idempotency-Key", KEY).contentType(MediaType.APPLICATION_JSON)
                .content(body(deltaGold));
    }

    private static String body(long deltaGold) {
        return "{\"characterId\": 7, \"deltaGold\": " + deltaGold + ", \"reason\": \"refund for ticket 123\"}";
    }

    private static AdjustmentView pending() {
        return new AdjustmentView(ID, 7, 50_000, "refund for ticket 123", AdjustmentStatus.PENDING_APPROVAL, true,
                RIN, Instant.parse("2026-09-22T10:00:00Z"), null, null, null);
    }

    private static AdjustmentView approvedBy(Operator approver) {
        return new AdjustmentView(ID, 7, 50_000, "refund for ticket 123", AdjustmentStatus.APPROVED, true,
                RIN, Instant.parse("2026-09-22T10:00:00Z"), approver, Instant.parse("2026-09-22T10:05:00Z"),
                "checked");
    }

    /** A token shaped like Keycloak's for this operator, with the production role mapping. */
    private JwtRequestPostProcessor operator(Operator operator, String... roles) {
        return jwt()
                .jwt(token -> token
                        .subject(operator.id())
                        .claim("preferred_username", operator.name())
                        .claim("realm_access", Map.of("roles", List.of(roles))))
                .authorities(new OperatorAuthoritiesConverter(operatorRoles));
    }
}
