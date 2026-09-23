package io.opentaan.admin.accounts;

/** No account has the requested id. */
public class AccountNotFoundException extends RuntimeException {

    private final long accountId;

    public AccountNotFoundException(long accountId) {
        super("No account with id " + accountId);
        this.accountId = accountId;
    }

    public long getAccountId() {
        return accountId;
    }
}
