package io.opentaan.admin.accounts;

import static org.hamcrest.Matchers.hasKey;
import static org.hamcrest.Matchers.not;
import static org.mockito.BDDMockito.given;
import static org.springframework.security.test.web.servlet.request.SecurityMockMvcRequestPostProcessors.jwt;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.content;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import java.util.List;
import java.util.Map;

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

/**
 * The HTTP contract of GET /api/accounts/{accountId}: authentication, the
 * permission check, validation, error format and the response shape.
 */
@WebMvcTest(AccountsController.class)
@Import(SecurityConfig.class)
class AccountsControllerTest {

    private static final String URL = "/api/accounts/{accountId}";

    @Autowired
    private MockMvc mvc;

    @Autowired
    private OperatorRoles operatorRoles;

    @MockitoBean
    private AccountQueryService accountQueries;

    @Test
    void refuses_a_request_without_a_token() throws Exception {
        mvc.perform(get(URL, 1)).andExpect(status().isUnauthorized());
    }

    @Test
    void refuses_an_operator_without_accounts_read() throws Exception {
        mvc.perform(get(URL, 1).with(operatorWithRoles("operations")))
                .andExpect(status().isForbidden());
    }

    @Test
    void returns_the_account_and_its_characters() throws Exception {
        given(accountQueries.getAccount(42)).willReturn(new AccountDetails(42, "player@example.com", List.of(
                new AccountDetails.Character(7, "Nova", 0),
                new AccountDetails.Character(9, "Ember", 1))));

        mvc.perform(get(URL, 42).with(operatorWithRoles("viewer")))
                .andExpect(status().isOk())
                .andExpect(content().contentTypeCompatibleWith(MediaType.APPLICATION_JSON))
                .andExpect(jsonPath("$.accountId").value(42))
                .andExpect(jsonPath("$.email").value("player@example.com"))
                .andExpect(jsonPath("$.characters[0].name").value("Nova"))
                .andExpect(jsonPath("$.characters[1].slot").value(1))
                .andExpect(jsonPath("$", not(hasKey("hashedPassword"))));
    }

    @Test
    void answers_404_with_problem_details_for_an_unknown_account() throws Exception {
        given(accountQueries.getAccount(404)).willThrow(new AccountNotFoundException(404));

        mvc.perform(get(URL, 404).with(operatorWithRoles("viewer")))
                .andExpect(status().isNotFound())
                .andExpect(content().contentTypeCompatibleWith(MediaType.APPLICATION_PROBLEM_JSON))
                .andExpect(jsonPath("$.title").value("Account not found"))
                .andExpect(jsonPath("$.status").value(404));
    }

    @Test
    void rejects_an_id_below_1() throws Exception {
        mvc.perform(get(URL, 0).with(operatorWithRoles("viewer")))
                .andExpect(status().isBadRequest())
                .andExpect(content().contentTypeCompatibleWith(MediaType.APPLICATION_PROBLEM_JSON))
                .andExpect(jsonPath("$.detail").value("accountId must be greater than or equal to 1"));
    }

    @Test
    void rejects_a_non_numeric_id() throws Exception {
        mvc.perform(get("/api/accounts/abc").with(operatorWithRoles("viewer")))
                .andExpect(status().isBadRequest());
    }

    /** A token shaped like Keycloak's, run through the same role mapping as production. */
    private JwtRequestPostProcessor operatorWithRoles(String... roles) {
        return jwt()
                .jwt(token -> token
                        .claim("preferred_username", "test-operator")
                        .claim("realm_access", Map.of("roles", List.of(roles))))
                .authorities(new OperatorAuthoritiesConverter(operatorRoles));
    }
}
