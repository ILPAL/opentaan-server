package io.opentaan.admin.accounts;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import io.opentaan.admin.TestcontainersConfiguration;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.transaction.annotation.Transactional;
import org.testcontainers.junit.jupiter.Testcontainers;

/**
 * Runs against a real PostgreSQL with the real migrations from database/ applied.
 *
 * <p>Starting the application at all is the first assertion: Hibernate validates
 * every mapping against the migrated schema and refuses to start on a mismatch.
 */
@SpringBootTest(
        properties = {
                "spring.flyway.enabled=true",
                "spring.flyway.locations=filesystem:../database/migrations",
        })
@Import(TestcontainersConfiguration.class)
@Testcontainers(disabledWithoutDocker = true)
@Transactional
class AccountQueryServiceIT {

    @Autowired
    private AccountQueryService accountQueries;

    @Autowired
    private JdbcTemplate jdbc;

    @Test
    void reads_an_account_with_its_characters_in_slot_order() {
        jdbc.update("DELETE FROM characters");
        jdbc.update("DELETE FROM accounts");
        jdbc.update("INSERT INTO accounts (account_id, email, hashed_password) VALUES (?, ?, ?)",
                1001L, "player@example.com", "v1$not-a-real-hash");
        insertCharacter(2002L, 1001L, 1, "Ember");
        insertCharacter(2001L, 1001L, 0, "Nova");

        var account = accountQueries.getAccount(1001L);

        assertThat(account.email()).isEqualTo("player@example.com");
        assertThat(account.characters())
                .extracting(AccountDetails.Character::name)
                .containsExactly("Nova", "Ember");
    }

    @Test
    void an_unknown_account_is_not_found() {
        assertThatThrownBy(() -> accountQueries.getAccount(999_999L))
                .isInstanceOf(AccountNotFoundException.class);
    }

    private void insertCharacter(long characterId, long accountId, int slot, String name) {
        jdbc.update("""
                INSERT INTO characters (character_id, account_id, slot_index, name, name_key, info_json)
                VALUES (?, ?, ?, ?, ?, '{}')
                """, characterId, accountId, slot, name, name.toLowerCase());
    }
}
