# How correctness is checked

A case study of how OpenTaan checks correctness, using one feature: gold
adjustments under dual control, and the audit trail that records them. The same
approach applies to the game server, where it is only starting.

## What "correct" means here

Both servers can fail in ways that tests usually miss.

- **The back office** can be wrong without any error: an adjustment approved
  by the person who asked for it, a retry that credits twice, or an audit
  record that someone quietly edited. Each of these returns 200.
- **The game server** answers a client that has no error messages. A wrong
  byte does not cause an exception; the client hangs or quits.

So the rule is that every property that matters is checked by something that
runs automatically, and where a mistake would cost money or evidence, it is
checked in two independent places.

## The layers, following one feature

### 1. The contract decides the shape

The adjustment API was written in `contracts/openapi/admin-api.yaml` before any
Java existed: the routes, the `Idempotency-Key` header and its pattern, the
amount limits, and the permission each operation needs. The build generates
interfaces from the file and the controllers implement them.

*What it catches:* a controller that drifts from the contract does not compile.
A request that breaks a declared limit is refused with 400 before any code of
ours runs, and `EconomyAdjustmentsControllerTest` checks that for the header,
the reason and the list limit.

### 2. Permissions cannot be forgotten

Each operation names its permission in the contract (`x-permission`) and in
the code (`@PreAuthorize`). The roles-to-permissions mapping is configuration,
validated at startup.

*What it catches:* `ArchitectureTest` fails the build if any public controller
method lacks a permission check. The controller tests sign requests with tokens
shaped like Keycloak's and run them through the production role mapping, so a
wrong mapping shows up as a wrong 403 or 200.

### 3. Rules live next to the data, and again in the database

The entity `EconomyAdjustment` enforces dual control: the requester can never
decide, and a decision happens once. The migration repeats the same rules as
constraints, such as `decider_is_not_requester` and `decision_matches_status`.

*What it catches:* `EconomyAdjustmentTest` covers the rules in memory. The
constraints catch any future path that skips the entity, such as a manual SQL
fix, a new service or the game server.

### 4. Concurrency is tested, not assumed

The expensive bugs happen when two requests arrive at the same moment.

- Two identical adjustment requests are released together by a latch. The
  unique key on (operator, key) lets one insert win, and the other request
  returns the winner's result. The test asserts one adjustment and one audit
  entry.
- Two operators decide the same adjustment at once, one approving and one
  rejecting. Exactly one decision stands and is audited. The other gets 409,
  whether it lost the version check or saw the first decision already
  committed.
- Eight threads append to the audit trail at once, and the chain must come
  out numbered without gaps and linked without forks. Before the trigger was
  trusted, a load run of sixteen clients inserting 1,600 entries was checked
  the same way.

The trigger's comment says that a writer under `REPEATABLE READ` fails rather
than forking the chain. That claim was tested by hand with two sessions before
it was written down, and the test showed the primary key refusing the second
writer.

### 5. Two implementations must agree

The database builds the audit hash chain (see
[ADR 0008](adr/0008-audit-chain-built-by-the-database.md)), and the admin server
recomputes it in Java to verify it. Two implementations of one definition can
drift apart, so:

- `AuditChainTest` pins the Java hash to a value taken from PostgreSQL, for an
  entry with non-ASCII text, so that UTF-8 byte lengths are exercised. When the
  value was first taken, a third, throwaway implementation produced the same
  hash.
- `AuditTrailIT` has the database write entries and the Java verifier check
  them.
- The same test switches the trigger off, edits or deletes an entry, and
  checks that verification reports that exact entry. It does this inside a
  transaction that is then rolled back; DDL is transactional in PostgreSQL, so
  the trigger and the entry come back.

### 6. The real database, the real migrations

Integration tests start PostgreSQL 18 in Testcontainers and apply the files in
`database/migrations`, the same ones Flyway applies in the stack. No schema is
generated for the tests, so a mapping that disagrees with a migration fails
the test run the way it would fail a deployment.

### 7. The whole stack, on every pull request

The `stack` workflow starts docker-compose and drives the feature end to end
with real tokens:

1. Two operators sign in through Keycloak.
2. One requests 50,000 gold, then retries and gets a replay.
3. The requester tries to approve and gets 403.
4. The second operator approves.
5. The job checks that the outbox holds the event once.
6. An auditor reads the adjustment's history: exactly two entries, by the
   right people.
7. The auditor verifies the chain: intact, three entries.
8. The job asks PostgreSQL directly to update and delete audit entries, as the
   table owner, and expects both to be refused.

## What the checks caught while building

- **A misleading error class.** The append-only trigger first raised SQLSTATE
  `42501` (insufficient privilege). Spring classifies every code in class 42 as
  a grammar error, so a refused audit edit was reported as "bad SQL grammar".
  An integration test that asserted the exception type caught it. The trigger
  now raises `23001` (restrict violation), which surfaces as a data integrity
  violation.
- **A contract that promised too much.** The verification schema said
  `brokenAt` was absent when the chain is intact. The server writes unset
  fields as `null`, and the controller test disagreed with the contract. The
  wording was corrected rather than papered over.
- **A README that promised too much.** The secrets README first claimed that
  no service ever holds a password in its environment. Checking what Flyway
  and Keycloak actually do showed that they do, after reading their files.
  The claim was narrowed to what is true for every service.
- **A vulnerable web server.** The first run of the image scan found three
  CRITICAL advisories in the Tomcat that Spring Boot 4.1.1 embeds, all fixed in a
  newer patch release that Spring Boot did not ship yet. The admin server now
  overrides the Tomcat version, with a comment saying when to remove the
  override.
- **A test runner that fails on nothing.** The .NET 10 test runner counts a
  test project with no tests as a failure, so the transport's test project
  waits for the transport instead of existing empty.

## The game server: bytes, not types

For the game protocol, correctness means agreeing with the client byte for
byte. Reference sessions record, for each message the client sends, the exact
bytes it expects back. The rule for every protocol test is to compare bytes,
not packet types or decoded values:

- A codec that writes a field in the wrong layout, and reads it back with the
  same mistake, passes a round-trip test. Only a byte comparison with a
  reference catches it.
- A test that only checks which packet was sent can pass while the bytes are
  wrong.

The reference sessions are derived from the game client's own files, so they
are not committed ([ADR 10](adr/0010-no-game-client-files.md)); they are
supplied locally. As the transport and codecs are built, each one is tested
against them before it is used, on the machine that has them.

## What is not checked yet

- **Our own code, statically:** CodeQL is not set up yet. Dependencies and
  images are scanned by the `security` workflow and kept current by
  Dependabot.
- **Load:** beyond the concurrency tests above, nothing measures throughput or
  latency.
- **The game protocol:** not in CI. The reference sessions it is checked
  against are not in the repository, so those checks run locally.
- **Refused attempts** are not recorded in the audit trail
  ([threat model](security/threat-model.md), T-R2).
