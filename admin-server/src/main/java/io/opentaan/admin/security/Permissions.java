package io.opentaan.admin.security;

import java.util.Set;

/**
 * Every permission an operator can hold. Each API operation requires exactly one,
 * named in its {@code x-permission} in the OpenAPI contract.
 *
 * <p>Reads and writes are separate permissions per subject: an operator who can
 * look at a session cannot end it on that basis alone.
 */
public final class Permissions {

    public static final String SESSIONS_READ = "sessions:read";
    public static final String SESSIONS_DISCONNECT = "sessions:disconnect";
    public static final String ROOMS_READ = "rooms:read";
    public static final String NODES_READ = "nodes:read";
    public static final String NODES_DRAIN = "nodes:drain";
    public static final String ACCOUNTS_READ = "accounts:read";
    public static final String CHARACTERS_READ = "characters:read";
    public static final String INVENTORY_READ = "inventory:read";
    public static final String MATCHES_READ = "matches:read";
    public static final String MODERATION_APPLY = "moderation:apply";
    public static final String LEDGER_READ = "ledger:read";
    public static final String ECONOMY_ADJUST = "economy:adjust";
    public static final String AUDIT_READ = "audit:read";

    public static final Set<String> ALL = Set.of(
            SESSIONS_READ, SESSIONS_DISCONNECT, ROOMS_READ, NODES_READ, NODES_DRAIN,
            ACCOUNTS_READ, CHARACTERS_READ, INVENTORY_READ, MATCHES_READ,
            MODERATION_APPLY, LEDGER_READ, ECONOMY_ADJUST, AUDIT_READ);

    private Permissions() {
    }
}
