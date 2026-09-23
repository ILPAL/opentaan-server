package io.opentaan.admin.security;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import java.io.IOException;
import java.util.List;
import java.util.Map;
import java.util.Set;

import org.junit.jupiter.api.Test;
import org.springframework.boot.context.properties.bind.Binder;
import org.springframework.boot.env.YamlPropertySourceLoader;
import org.springframework.core.env.StandardEnvironment;
import org.springframework.core.io.ClassPathResource;

/** Checks the role table actually shipped in application.yml, not a copy of it. */
class OperatorRolesTest {

    private final OperatorRoles shipped = loadShippedRoles();

    @Test
    void admin_holds_every_permission() {
        assertThat(shipped.permissionsFor(List.of("admin"))).isEqualTo(Permissions.ALL);
    }

    @Test
    void every_permission_is_granted_by_some_role_other_than_admin() {
        // "Admin only" should be a decision, not something that happens because a
        // new permission was never given a home.
        var nonAdminRoles = shipped.roles().keySet().stream().filter(role -> !role.equals("admin")).toList();

        assertThat(shipped.permissionsFor(nonAdminRoles)).isEqualTo(Permissions.ALL);
    }

    @Test
    void reading_a_subject_does_not_grant_changing_it() {
        assertThat(shipped.permissionsFor(List.of("viewer")))
                .contains(Permissions.SESSIONS_READ)
                .doesNotContain(Permissions.SESSIONS_DISCONNECT, Permissions.ECONOMY_ADJUST, Permissions.NODES_DRAIN);
    }

    @Test
    void an_unknown_role_grants_nothing() {
        assertThat(shipped.permissionsFor(List.of("superuser"))).isEmpty();
    }

    @Test
    void a_misspelled_permission_stops_the_application_starting() {
        assertThatThrownBy(() -> new OperatorRoles(Map.of("viewer", Set.of("acounts:read"))))
                .isInstanceOf(IllegalArgumentException.class)
                .hasMessageContaining("acounts:read");
    }

    private static OperatorRoles loadShippedRoles() {
        try {
            var environment = new StandardEnvironment();
            new YamlPropertySourceLoader()
                    .load("application.yml", new ClassPathResource("application.yml"))
                    .forEach(environment.getPropertySources()::addLast);
            return Binder.get(environment).bind("opentaan.security", OperatorRoles.class).get();
        } catch (IOException e) {
            throw new IllegalStateException("Cannot read application.yml", e);
        }
    }
}
