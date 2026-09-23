package io.opentaan.admin.security;

import java.util.Map;
import java.util.Set;
import java.util.TreeSet;
import java.util.stream.Collectors;

import org.springframework.boot.context.properties.ConfigurationProperties;

/**
 * Which permissions each operator role grants, from {@code opentaan.security.roles}.
 *
 * <p>Roles come from the identity provider; permissions are decided here. Keeping
 * the mapping in this service means changing what a role may do is a reviewed code
 * change, not an edit in the identity provider's admin console.
 *
 * @param roles role name to the permissions it grants
 */
@ConfigurationProperties("opentaan.security")
public record OperatorRoles(Map<String, Set<String>> roles) {

    public OperatorRoles {
        roles = roles == null ? Map.of() : Map.copyOf(roles);
        var unknown = roles.values().stream()
                .flatMap(Set::stream)
                .filter(permission -> !Permissions.ALL.contains(permission))
                .collect(Collectors.toCollection(TreeSet::new));
        if (!unknown.isEmpty()) {
            throw new IllegalArgumentException("Unknown permissions in opentaan.security.roles: " + unknown);
        }
    }

    /** The union of the permissions granted by the given roles. Unknown roles grant nothing. */
    public Set<String> permissionsFor(Iterable<String> roleNames) {
        var granted = new TreeSet<String>();
        for (var role : roleNames) {
            granted.addAll(roles.getOrDefault(role, Set.of()));
        }
        return granted;
    }
}
