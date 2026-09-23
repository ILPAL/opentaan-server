package io.opentaan.admin.economy;

/** The adjustment names a character that does not exist. */
public class CharacterNotFoundException extends RuntimeException {

    public CharacterNotFoundException(long characterId) {
        super("No character with id " + characterId);
    }
}
