# 11. The game server keeps what lasts in PostgreSQL, as its only copy, through SQL it writes

- Status: Accepted
- Date: 2026-11-22

## Context

The game server held accounts and characters in memory, so a restart lost every
account, every character and every finished tutorial. They belong in the
PostgreSQL database the admin server already reads, whose schema the migrations
own ([0004](0004-migrations-own-the-schema.md)).

A game server can keep its data in two ways: in memory, writing changes to a
database behind it, or in the database alone. The first reads quickly, but it
keeps two copies that can disagree. A write can fail after the client has been
told it happened, or be lost when the process stops before it is written, and ids
handed out from a counter in memory can fall behind the rows already there. What
this server keeps is read when a player logs in and on the character screen, not
on every shot.

The schema will keep growing, one feature at a time, while both services run
against it. A change to it must never stop either service working.

## Decision

- **The database is the only copy** of what lasts beyond a connection. Every
  request reads it and waits for each of its writes; a write that fails ends the
  request, and with it the connection, so a client is never told of something that
  was not kept. What lasts only for a room or a match stays in memory.
- **SQL written by hand,** through Npgsql. Every query names its columns. Only the
  code in `Storage` uses the driver, which an architecture test enforces; the rest
  of the server works through the stores' interfaces.
- **The database hands out ids,** from identity columns, so no counter can fall
  behind the rows.
- **The server checks the schema at startup** and refuses to start when the
  migration it needs is missing from Flyway's record of what it applied. A newer
  schema is accepted.
- **Migrations expand, then contract.** Each one keeps working with the release of
  each service before it: a column is added with a default or as nullable, never
  renamed or retyped in place, and dropped only in a later migration, once no
  release reads it. So a database can be upgraded before the services that use it.
- **The database holds the client's limits too:** every value the client is sent
  fits the field it is sent in, by a constraint, so no bug can store one the
  client cannot read.
- **Every store has an in-memory twin** for the fast tests, and one contract runs
  against both. The PostgreSQL side runs on a real PostgreSQL 18 with the real
  migrations, applied by Flyway in the image the deployment runs, and in CI it
  never skips.

## Alternatives considered

- **Entity Framework Core,** without its migrations, which [0004](0004-migrations-own-the-schema.md)
  rules out. It would generate the SQL from a model that only the .NET side can
  read, and the queries here are few and small.
- **Dapper** maps rows to objects for less code per query. It saves a few lines
  per store and adds a dependency; the SQL is the same.
- **A copy in memory, written behind** to the database. It reads faster, at the
  cost of the disagreements above, for reads that happen at login.
- **A document per character,** in JSON. A new field needs no migration, but the
  database can check none of the values, and a renamed key leaves old documents
  that read differently.

## Consequences

- A restart loses nothing a player made: accounts, characters and finished
  tutorials come back as they were.
- Logins and character screens each wait on the database. With the database down,
  they fail and their connections close; nothing is lost or half-written.
- Adding a field is a migration with a default, then the code that uses it. A
  migration cannot break the release running before it, and the schema is checked
  twice: by each service at startup, and by the tests against the real migrations.
- The storage tests need Docker, which CI has, or a PostgreSQL named for them.
