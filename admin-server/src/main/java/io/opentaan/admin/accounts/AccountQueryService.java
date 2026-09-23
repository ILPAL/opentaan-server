package io.opentaan.admin.accounts;

import java.util.Map;

import io.opentaan.admin.audit.AuditActions;
import io.opentaan.admin.audit.AuditTrail;
import io.opentaan.admin.security.Operator;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

/** Read side of the accounts feature. Knows nothing about HTTP. */
@Service
public class AccountQueryService {

    private static final String SUBJECT_TYPE = "account";

    private final AccountRecordRepository accounts;
    private final CharacterRecordRepository characters;
    private final AuditTrail auditTrail;

    AccountQueryService(AccountRecordRepository accounts, CharacterRecordRepository characters,
            AuditTrail auditTrail) {
        this.accounts = accounts;
        this.characters = characters;
        this.auditTrail = auditTrail;
    }

    /**
     * The account with its characters. An account holds a player's personal data,
     * so each read is recorded in the audit trail.
     *
     * @throws AccountNotFoundException if no account has this id
     */
    @Transactional
    public AccountDetails getAccount(Operator viewer, long accountId) {
        var account = accounts.findById(accountId)
                .orElseThrow(() -> new AccountNotFoundException(accountId));
        var characterList = characters.findByAccountIdOrderBySlotIndexAsc(accountId).stream()
                .map(c -> new AccountDetails.Character(
                        c.getCharacterId(), c.getName(), Math.toIntExact(c.getSlotIndex())))
                .toList();
        auditTrail.record(viewer, AuditActions.ACCOUNT_VIEWED, SUBJECT_TYPE, Long.toString(accountId), Map.of());
        return new AccountDetails(account.getAccountId(), account.getEmail(), characterList);
    }

    @Transactional(readOnly = true)
    public boolean characterExists(long characterId) {
        return characters.existsById(characterId);
    }
}
