package io.opentaan.admin.security;

import java.util.Objects;

/**
 * The person behind a request.
 *
 * @param id   stable identifier from the identity provider (the token's subject);
 *             used to tell operators apart, for example for the four-eyes rule
 * @param name human-readable name (the token's preferred_username), for display only
 */
public record Operator(String id, String name) {

    public Operator {
        Objects.requireNonNull(id, "id");
        Objects.requireNonNull(name, "name");
    }

    public boolean isSamePersonAs(String otherId) {
        return id.equals(otherId);
    }
}
