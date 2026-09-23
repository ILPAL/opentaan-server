package io.opentaan.admin.security;

import org.springframework.security.core.context.SecurityContextHolder;
import org.springframework.security.oauth2.server.resource.authentication.JwtAuthenticationToken;
import org.springframework.stereotype.Component;

/** Reads the signed-in operator from the current request's access token. */
@Component
public class CurrentOperator {

    public Operator get() {
        var authentication = SecurityContextHolder.getContext().getAuthentication();
        if (!(authentication instanceof JwtAuthenticationToken token)) {
            throw new IllegalStateException("No operator is signed in");
        }
        var jwt = token.getToken();
        var name = jwt.getClaimAsString("preferred_username");
        return new Operator(jwt.getSubject(), name != null ? name : jwt.getSubject());
    }
}
