package io.opentaan.admin.accounts;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import org.hibernate.annotations.Immutable;

/**
 * A row of {@code accounts}, read-only. The password hash column is deliberately
 * not mapped, so no code path in this service can load or return it.
 */
@Entity
@Immutable
@Table(name = "accounts")
public class AccountRecord {

    @Id
    @Column(name = "account_id")
    private Long accountId;

    @Column(name = "email", nullable = false)
    private String email;

    protected AccountRecord() {
    }

    public Long getAccountId() {
        return accountId;
    }

    public String getEmail() {
        return email;
    }
}
