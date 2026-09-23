package io.opentaan.admin.accounts;

import io.opentaan.admin.api.AccountsApi;
import io.opentaan.admin.api.model.Account;
import io.opentaan.admin.api.model.CharacterSummary;
import io.opentaan.admin.security.Permissions;
import org.springframework.http.ResponseEntity;
import org.springframework.security.access.prepost.PreAuthorize;
import org.springframework.web.bind.annotation.RestController;

/**
 * Implements the Accounts operations of the OpenAPI contract. Routes, parameter
 * validation and response types come from the generated {@link AccountsApi}; this
 * class only checks the permission and maps between the service and the API model.
 */
@RestController
class AccountsController implements AccountsApi {

    private final AccountQueryService accountQueries;

    AccountsController(AccountQueryService accountQueries) {
        this.accountQueries = accountQueries;
    }

    @Override
    @PreAuthorize("hasAuthority('" + Permissions.ACCOUNTS_READ + "')")
    public ResponseEntity<Account> getAccount(Long accountId) {
        return ResponseEntity.ok(toApi(accountQueries.getAccount(accountId)));
    }

    private static Account toApi(AccountDetails account) {
        var characters = account.characters().stream()
                .map(c -> new CharacterSummary(c.characterId(), c.name(), c.slot()))
                .toList();
        return new Account(account.accountId(), account.email(), characters);
    }
}
