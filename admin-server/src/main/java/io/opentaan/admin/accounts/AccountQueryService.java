package io.opentaan.admin.accounts;

import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

/** Read side of the accounts feature. Knows nothing about HTTP. */
@Service
public class AccountQueryService {

    private final AccountRecordRepository accounts;
    private final CharacterRecordRepository characters;

    AccountQueryService(AccountRecordRepository accounts, CharacterRecordRepository characters) {
        this.accounts = accounts;
        this.characters = characters;
    }

    /**
     * The account with its characters.
     *
     * @throws AccountNotFoundException if no account has this id
     */
    @Transactional(readOnly = true)
    public AccountDetails getAccount(long accountId) {
        var account = accounts.findById(accountId)
                .orElseThrow(() -> new AccountNotFoundException(accountId));
        var characterList = characters.findByAccountIdOrderBySlotIndexAsc(accountId).stream()
                .map(c -> new AccountDetails.Character(
                        c.getCharacterId(), c.getName(), Math.toIntExact(c.getSlotIndex())))
                .toList();
        return new AccountDetails(account.getAccountId(), account.getEmail(), characterList);
    }
}
