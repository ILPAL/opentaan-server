package io.opentaan.admin.security;

import org.springframework.boot.context.properties.EnableConfigurationProperties;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.security.config.annotation.method.configuration.EnableMethodSecurity;
import org.springframework.security.config.annotation.web.builders.HttpSecurity;
import org.springframework.security.config.http.SessionCreationPolicy;
import org.springframework.security.oauth2.server.resource.authentication.JwtAuthenticationConverter;
import org.springframework.security.web.SecurityFilterChain;

/**
 * Every API route needs a bearer token; each operation then checks its own
 * permission with {@code @PreAuthorize}. The only unauthenticated routes are the
 * health probe and the API documentation.
 */
@Configuration
// Class-based proxies: controllers implement generated interfaces, and an
// interface-based proxy would hide their @RestController annotation from Spring MVC.
@EnableMethodSecurity(proxyTargetClass = true)
@EnableConfigurationProperties(OperatorRoles.class)
public class SecurityConfig {

    @Bean
    SecurityFilterChain apiSecurity(HttpSecurity http, OperatorRoles operatorRoles) throws Exception {
        var tokens = new JwtAuthenticationConverter();
        tokens.setJwtGrantedAuthoritiesConverter(new OperatorAuthoritiesConverter(operatorRoles));
        tokens.setPrincipalClaimName("preferred_username");

        return http
                .authorizeHttpRequests(requests -> requests
                        .requestMatchers("/actuator/health/**", "/openapi/**", "/swagger-ui/**", "/swagger-ui.html",
                                "/v3/api-docs/swagger-config")
                        .permitAll()
                        .anyRequest().authenticated())
                .oauth2ResourceServer(server -> server.jwt(jwt -> jwt.jwtAuthenticationConverter(tokens)))
                .sessionManagement(sessions -> sessions.sessionCreationPolicy(SessionCreationPolicy.STATELESS))
                // Stateless bearer-token API: no cookies, so no CSRF surface.
                .csrf(csrf -> csrf.disable())
                .build();
    }
}
