package io.opentaan.admin.accounts;

import java.util.List;

/**
 * An account as the back office sees it. Independent of both the database mapping
 * and the API model, so either can change without touching the other.
 */
public record AccountDetails(long accountId, String email, List<Character> characters) {

    public AccountDetails {
        characters = List.copyOf(characters);
    }

    /** One character on the account, in selection-screen order. */
    public record Character(long characterId, String name, int slot) {
    }
}
