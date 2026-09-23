# 9. Secrets are files, and no credentials are committed

- Status: Accepted
- Date: 2026-09-22

## Context

The local stack needs a database password and a Keycloak admin password.
Passwords in a compose file end up in git. Passwords in environment variables
show up in `docker inspect`, in process listings and in crash reports. The
realm also needs operators to sign in, and committed test users would be
credentials in the repository.

## Decision

- **Every password is a file** in `secrets/`, created locally from a random
  source and ignored by git. Compose mounts each one as a Docker secret at
  `/run/secrets/<name>`.
- **Each service reads its file inside its own container.** PostgreSQL uses
  `POSTGRES_PASSWORD_FILE`. The admin server imports `/run/secrets/` as
  configuration (`configtree:`), so the password is never an environment
  variable. Flyway and Keycloak read theirs in their entrypoint.
- **No users in the realm file.** Operators are added to a running stack with
  `deploy/keycloak/add-operator.sh`, which asks for the password, or reads it
  from `OPERATOR_PASSWORD` in CI.
- **CI makes its own secrets** with `openssl rand` for each run.

## Alternatives considered

- **An `.env` file with the passwords.** This is common, but every value
  becomes an environment variable of the container.
- **A secrets manager such as Vault.** It is the right tool for a shared
  environment, but too much for a stack that has to start with
  `docker compose up`. Services that read secrets from files move to a manager
  easily, because the manager can write the same files.

## Consequences

- No password is in the repository, an image, the compose file or
  `docker inspect`.
- Flyway and Keycloak still hold their password in their own process
  environment after startup. The claim is limited to what is true for every
  service.
- Rotation is manual: change the password in the service, write the new file,
  restart the services that read it.
