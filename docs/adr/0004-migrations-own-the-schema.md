# 4. The migrations own the schema; the services only validate it

- Status: Accepted
- Date: 2026-09-22

## Context

The game server and the admin server use one PostgreSQL database. If each
service migrated the schema at startup, two services would be editing the same
tables. The order of their deployments would then decide the schema, and a
rollback of one service could not undo what the other had applied.

## Decision

The schema is defined only by `database/migrations`, one change per file, and
applied by Flyway as its own step before either service starts. In
docker-compose this is a container that the servers wait on. The services
never run DDL. At startup the admin server's Hibernate checks its mappings
against the schema (`ddl-auto: validate`) and refuses to start on a mismatch.
Integration tests apply the same migration files to a real PostgreSQL.

Rules that protect money or records are written as constraints and triggers in
the migrations, as well as in the code (see
[0006](0006-dual-control-and-idempotency-enforced-twice.md) and
[0008](0008-audit-chain-built-by-the-database.md)).

## Alternatives considered

- **Flyway inside the admin server.** Simple with one service. With two, it
  makes the admin server's release schedule the game server's schema
  schedule.
- **EF Core migrations in the game server.** This has the same problem the
  other way round, and it hides SQL behind a model that the Java side cannot
  read.

## Consequences

- A migration ships in the same pull request as the code that needs it.
- A migration that has run anywhere is never edited. Flyway's checksums
  enforce this, and mistakes are fixed by the next migration.
- Neither service can quietly drift from the schema: the admin server fails
  fast, and its tests run against the real migrations, not a generated schema.
- Database rules written in SQL are a second language to review, and each one
  needs a test that proves the database, not only the code, enforces it.
