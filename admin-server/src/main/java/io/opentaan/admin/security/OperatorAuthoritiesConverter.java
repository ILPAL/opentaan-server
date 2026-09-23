package io.opentaan.admin.security;

import java.util.Collection;
import java.util.List;
import java.util.Map;

import org.springframework.core.convert.converter.Converter;
import org.springframework.security.core.GrantedAuthority;
import org.springframework.security.core.authority.SimpleGrantedAuthority;
import org.springframework.security.oauth2.jwt.Jwt;

/**
 * Turns the realm roles in a Keycloak access token into permission authorities.
 *
 * <p>Keycloak puts realm roles under {@code realm_access.roles}. Each role is
 * expanded to its permissions through {@link OperatorRoles}, so endpoints check
 * permissions such as {@code accounts:read} and never role names.
 */
public final class OperatorAuthoritiesConverter implements Converter<Jwt, Collection<GrantedAuthority>> {

    private final OperatorRoles operatorRoles;

    public OperatorAuthoritiesConverter(OperatorRoles operatorRoles) {
        this.operatorRoles = operatorRoles;
    }

    @Override
    public Collection<GrantedAuthority> convert(Jwt jwt) {
        return operatorRoles.permissionsFor(realmRoles(jwt)).stream()
                .<GrantedAuthority>map(SimpleGrantedAuthority::new)
                .toList();
    }

    private static List<String> realmRoles(Jwt jwt) {
        Map<String, Object> realmAccess = jwt.getClaimAsMap("realm_access");
        if (realmAccess == null || !(realmAccess.get("roles") instanceof Collection<?> roles)) {
            return List.of();
        }
        return roles.stream()
                .filter(String.class::isInstance)
                .map(String.class::cast)
                .toList();
    }
}
