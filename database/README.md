# Database

PostgreSQL 18 schema for the game server and the admin server.

Both services share one database, so neither of them owns the schema. The
migrations in this folder do: a Flyway container applies them before either
service starts, and each service only checks at startup that the schema is
what it expects.

## Rules

- **One change per file.** Name it `V<next number>__<what_it_does>.sql`.
- **Never edit a migration that has run anywhere.** Flyway stores a checksum
  of every applied file and refuses to start if one changes. Fix a mistake
  with the next migration.
- **Ship the migration with the code that needs it,** in the same pull request.
- **Services never run DDL.** If a service needs a table, it gets a migration
  here.

## Files

| Migration | What it does |
|---|---|
| `V1__initial_schema.sql` | Accounts, characters, state documents, idempotency claims, shop ledger, operators, audit trail, match history |
| `V2__economy_adjustments.sql` | Gold adjustments under four-eyes approval, enforced by constraints; the outbox for events to the game server |
| `V3__audit_trail.sql` | The audit trail: append-only, numbered and hash-chained by triggers, so no service can change an entry or place one outside the chain |
