# 5. Operators sign in through Keycloak; roles live there, permissions in code

- Status: Accepted
- Date: 2026-09-22

## Context

Operators need to sign in, and each operation of the admin API needs an
authorization check. Who an operator is and which job they do change often and
belong to whoever runs the service. What a job is allowed to do is a security
decision that should be reviewed like code.

## Decision

- **Identity:** Keycloak, over OpenID Connect. The admin server is a resource
  server that validates bearer tokens against the realm's signing keys and
  issuer. Tokens live for five minutes. The realm has brute-force protection,
  no self-registration and no password reset.
- **Roles:** realm roles in Keycloak, such as `viewer`, `economy` and `auditor`,
  describe jobs.
- **Permissions:** `opentaan.security.roles` in the admin server's
  `application.yml` maps each role to permissions such as `accounts:read` and
  `economy:adjust`. At startup, a permission that does not exist in
  `Permissions` stops the service.
- **Checks:** every endpoint requires exactly one permission, with
  `@PreAuthorize`, and names it in the contract's `x-permission`. An
  architecture test fails the build if a public controller method has no
  check.
- **Operators are not committed.** The realm file has no users.
  `deploy/keycloak/add-operator.sh` adds them to a running stack.

## Alternatives considered

- **Checking role names in code.** Changing what a role may do would then be
  a search-and-replace across controllers, and a role would slowly become a
  bundle of unrelated powers.
- **Permissions in Keycloak** (client scopes or Authorization Services). This
  moves security decisions into an admin console, where changes have no
  review, no history in the repository and no tests.
- **The service's own login.** This means building password storage, lockout
  and session handling that an identity provider already does better, and
  giving up single sign-on and MFA.

## Consequences

- Granting a job a new power is a pull request that changes `application.yml`
  and passes the role-mapping tests.
- The browser client uses authorization code with PKCE. The command-line
  client uses the password grant and exists for local development only.
- Tokens are not yet checked for audience. Any client of the realm can call
  the API, which is acceptable while the realm has only operator clients (see
  the [threat model](../security/threat-model.md)).
- MFA is not configured yet. It is a realm setting, and no code has to change.
