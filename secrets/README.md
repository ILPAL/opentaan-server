# Secrets

Files in this folder are mounted into containers as Docker secrets by
`docker-compose.yml`. They never go into git (see `.gitignore` here), into an
image or into the compose file, so they do not show up in `docker inspect`.
Each service reads its file at startup, inside its own container.

Create them once, before the first `docker compose up`:

```sh
openssl rand -base64 32 | tr -d '\n' > secrets/db_password
openssl rand -base64 32 | tr -d '\n' > secrets/keycloak_admin_password
chmod 600 secrets/db_password secrets/keycloak_admin_password
```

`tr -d '\n'` matters: a trailing newline would become part of the password.

| File | Used by |
|---|---|
| `db_password` | PostgreSQL (`POSTGRES_PASSWORD_FILE`), Flyway, the admin server |
| `keycloak_admin_password` | Keycloak's `admin` user in the `master` realm, and `deploy/keycloak/add-operator.sh` |

To rotate one, change it in the service first (for example
`ALTER ROLE opentaan PASSWORD '...'`), write the new value here, then restart the
services that read it. Secrets are read at startup only.
