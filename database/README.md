# Database

PostgreSQL 18 schema for the game server and the admin server.

Both services share one database, so neither of them owns the schema. The
migrations in this folder do: a Flyway container applies them before either
service starts, and each service only checks at startup that the schema is
what it expects. The admin server checks its mappings against the tables; the
game server checks that Flyway has applied the migration it needs, and refuses
to start otherwise.

## Rules

- **One change per file.** Name it `V<next number>__<what_it_does>.sql`.
- **Never edit a migration that has run anywhere.** Flyway stores a checksum
  of every applied file and refuses to start if one changes. Fix a mistake
  with the next migration.
- **Ship the migration with the code that needs it,** in the same pull request.
- **Services never run DDL.** If a service needs a table, it gets a migration
  here.
- **Expand, then contract.** Every migration keeps working with the release of
  each service before it, so the database can be upgraded before the services:
  - add a column with a default, or as nullable;
  - never rename or retype a column in place: add the new one, move the code to
    it, and drop the old one in a later migration, once no release reads it;
  - a constraint added to a table must hold for every row already in it.
- **Values the client is sent fit the client.** A column holding a value the game
  client is sent carries a check that it fits the field it is sent in, so no bug
  can store one the client cannot read.
- **Name every column.** Queries name the columns they read and write, never
  `SELECT *` or an `INSERT` without columns, so a column added later breaks
  nothing.

## Files

| Migration | What it does |
|---|---|
| `V1__initial_schema.sql` | Accounts, characters, state documents, idempotency claims, shop ledger, operators, audit trail, match history |
| `V2__economy_adjustments.sql` | Gold adjustments under four-eyes approval, enforced by constraints; the outbox for events to the game server |
| `V3__audit_trail.sql` | The audit trail: append-only, numbered and hash-chained by triggers, so no service can change an entry or place one outside the chain |
| `V4__accounts_kept_by_the_game_server.sql` | Accounts as the game server keeps them: ids handed out by the database from 1001 and players from 1, cash, a password always given, and every value within what the client can carry |
