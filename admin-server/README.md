# Admin server

Spring Boot 4 back office for operators: account lookup and gold adjustments
under four-eyes approval today; the audit trail and moderation next.

## How it is put together

- **Contract first.** The API is defined in
  [`contracts/openapi/admin-api.yaml`](../contracts/openapi/admin-api.yaml).
  The build generates Java interfaces from it; controllers implement them, so
  the code cannot drift from the contract. Swagger UI serves that same file at
  `/swagger-ui.html`.
- **Permissions, not roles, in the code.** Operators log in through the identity
  provider (Keycloak). Their realm roles are mapped to permissions in
  `application.yml`, and every endpoint checks one permission with
  `@PreAuthorize`. An architecture test fails the build if an endpoint has none.
- **The schema belongs to `database/`.** This service never runs DDL. At
  startup Hibernate checks that its mappings match the schema and refuses to
  start if they do not.
- **Read-only where it should be.** Account data is mapped with `@Immutable`
  entities behind repositories that have no save or delete methods, and the
  password hash column is not mapped at all.

## Economy adjustments

An operator with `economy:adjust` requests a change to a character's gold with
an `Idempotency-Key` header.

- **Below the approval threshold** (`opentaan.economy.approval-threshold-gold`,
  10,000 by default) the adjustment is approved at once.
- **At or above it,** a second operator with `economy:adjust` must approve or
  reject it. The requester can never decide their own adjustment; the database
  refuses it too (`decider_is_not_requester`).
- **Retries are safe.** The same key with the same body returns the original
  adjustment with `Idempotent-Replayed: true`; the same key with a different body
  is refused with 409. Two identical requests arriving at once still create one
  adjustment, because the key is unique per operator in the database.
- **Approval queues delivery.** An approved adjustment is written to
  `outbox_events` in the same transaction, for the game server to apply. Nothing
  is sent for a rejection. Two operators deciding at once cannot both win:
  optimistic locking answers the second with 409.

## Build and test

```sh
./mvnw verify
```

Unit tests run everywhere. Integration tests start PostgreSQL 18 in Docker
(Testcontainers) and apply the real migrations; without Docker they are skipped.

## Run locally

With Docker, `TestAdminServerApplication` (in `src/test`) starts the server
against a throwaway database. Against your own PostgreSQL:

```sh
OPENTAAN_DB_URL=jdbc:postgresql://localhost:5432/opentaan \
OPENTAAN_DB_USER=opentaan OPENTAAN_DB_PASSWORD=... \
OPENTAAN_OIDC_ISSUER=http://localhost:8081/realms/opentaan \
./mvnw spring-boot:run
```

Health: `GET /actuator/health`. Everything under `/api` needs a bearer token.

## Docker

The image is built from the repository root, because code generation needs
`contracts/`:

```sh
docker build -f admin-server/Dockerfile .
```
