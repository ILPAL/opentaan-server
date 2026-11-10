# Roadmap

Where OpenTaan stands against its plan: what was decided at the start, what is
built, and what comes next. Last updated 2026-11-10.

## Goals

1. **Bring the game back.** A server the TAAN game client can connect to and play on.
2. **Build it to the standard of systems that move money.** Contracts written before
   code, a permission check on every operation, dual control over value changes,
   safe retries, an audit trail, and automated checks that enforce all of it.

## The plan

### Decisions taken at the start

| Area | Decision | State |
|---|---|---|
| Repository | One repository: game server, admin server, contracts, database, deployment | Done |
| Game server | .NET 10; projects added one per stage (transport first) | Done: transport, host, connection sessions, and the login, character, tutorial, and channel and lobby stages, creating, entering and leaving rooms, and what players do in a waiting room |
| Admin server | Spring Boot 4 on Java 25, built with Maven | Done |
| Shared contracts | `contracts/`: OpenAPI for the admin API, JSON Schemas for the control channel; the game protocol's reference data is supplied locally, not committed | Partly: OpenAPI done; control-channel schemas not started |
| Database schema | Owned by `database/migrations`, applied by Flyway; services only validate it | Done |
| Operator login | Keycloak (OpenID Connect); roles in Keycloak, permissions in code | Done |
| Admin UI | Operator portal served by the admin server | Not started; Swagger UI shows the API meanwhile |
| Game client files | Never committed; supplied locally and mounted at runtime | Done: none in the repository. Mounting comes with the first code that reads them |
| Finance controls | Four-eyes approval, idempotency keys, append-only audit trail; double-entry ledger optional | Partly: four-eyes, idempotency and the audit trail done; ledger not planned yet |
| Quality in CI | Testcontainers, architecture rules as tests, security scanning, metrics dashboards | Partly: Testcontainers, architecture rules, image scanning and Dependabot done; CodeQL and dashboards not started |

### First showable version (v1)

What a reviewer should be able to see and run, end to end.

- [x] `docker compose up` starts PostgreSQL, Flyway, Keycloak and both servers
- [x] Operators sign in through Keycloak; every endpoint checks one permission
- [x] Account lookup through a contract-first API
- [x] Gold adjustments with four-eyes approval and idempotency keys
- [x] Integration tests on real PostgreSQL; architecture rules as tests
- [x] CI runs the whole stack and drives a real request through it
- [x] README: what it is, how to run it, how correctness is checked
- [x] Append-only audit trail for every operator action
- [x] Game server transport: framing, the packet cipher and the seed schedule, with the reference sessions replayed through it
- [x] Game server sessions: a real client connects, logs in, and creates and selects a character; the reference sessions are played against real connections
- [ ] Packet codecs, compared byte for byte with the reference sessions (started: the handshake, the account and login packets, the character packets, the tutorial's reports, the channel and lobby packets, and the room packets)
- [ ] Security scanning in CI: CodeQL (Dependabot, Trivy and an SBOM per image are done)
- [x] Decision records, a threat model and a written case study in `docs/`

## Done

| Area | What exists |
|---|---|
| Repository setup | `.gitignore`, `.gitattributes`, `.editorconfig` |
| Database | `V1` initial schema (11 tables); `V2` economy adjustments and the outbox, with the approval rules as constraints; `V3` the audit trail |
| Game server | .NET 10 solution: transport framing and the packet cipher, handshake keys that can never switch encryption off, codecs for the handshake, the account and login packets, the character packets, the channel and lobby packets and the room packets, a session per connection with connection limits, idle and send timeouts, account check, creation and login with accounts in memory, one live connection per account, character naming, creation, selection and deletion with characters in memory and one character per account, rooms created, entered, quick-joined and left in each channel's lobby, with mission rooms and connections playing two accounts, and what players do in a waiting room (ready, team, weapon, the host's options with the maps the client lists for each channel mode, slots, expulsions), the reference sessions replayed through the codecs and played against real connections, architecture rules |
| Admin server | Contract-first API, Keycloak token validation, role-to-permission mapping, read-only account data, problem-details errors |
| Local stack | docker-compose with PostgreSQL 18, Flyway, Keycloak and both servers; secrets as files; operator script with no committed credentials |
| CI | `game-server`, `admin-server` and `stack` workflows; the stack job signs operators in, takes an adjustment through four-eyes approval and checks the audit trail it left |
| Economy adjustments | Request, list, approve, reject; threshold for dual control; idempotent retries, including two identical requests at once; transactional outbox |
| Audit trail | An entry for every change and every account read, in the same transaction as the action; the database refuses `UPDATE`, `DELETE` and `TRUNCATE`, and numbers and hash-chains entries itself; a Java verifier recomputes the chain; list and verification endpoints behind `audit:read` |
| Documentation | Project README; a README per service and per shared folder |
| Decisions and security | Ten decision records in `docs/adr`; a threat model with each threat's control, test and status; a case study of how correctness is checked; a test that two concurrent decisions leave exactly one |
| Security scanning | `security` workflow: both images built, scanned with Trivy on every change and weekly, failing on fixable HIGH or CRITICAL findings, with a CycloneDX SBOM kept per image; Dockerfile checks; Dependabot for Maven, NuGet, base images, compose images and actions; every action pinned to a commit hash |

Tests today: game server 610, of which 21 need the reference sessions and skip without them; admin server 61 unit tests and 25 integration tests.

## Next

In order. Each item is one pull request unless noted.

1. **Packet codecs, continued.** `OpenTaan.Protocol`, one stage of the protocol
   at a time, each codec compared byte for byte with the reference sessions before
   it is used. The handshake, the account and login packets, and the character
   packets (creation, name checks, selection and deletion), the tutorial, cash,
   play time, channel selection, the lobby with the players and rooms in it, and
   creating, entering, quick-joining and leaving rooms, and what players do in a
   waiting room (getting ready, changing team or weapon, the room's options, opening
   and closing slots, and expelling a player) are done and served by real sessions.
   Matches come next, missions first. Starting a match also turns a random map or
   weapon pick into a real one, which the client cannot do itself: told to play a
   random map, it shows a black screen.
2. **CodeQL.** Static analysis of the Java and C# code, reported through GitHub
   code scanning.
3. **Operator portal.** Pages served by the admin server: account lookup, requesting
   adjustments and an approval queue, signing in with the `admin-portal` client (PKCE).

## Later

- **Arena rooms.** Rooms in arena channels, whose room reply and slot changes
  carry the names of the two teams. The room codecs carry those names already,
  and this server sends them empty until then.
- **Control channel.** The signed connection from the admin server to the game
  server: `contracts/control-channel` schemas first, then live actions such as
  disconnecting a session, and a dispatcher that delivers approved adjustments
  from the outbox.
- **Game server stages.** PostgreSQL access (`OpenTaan.Persistence`) and game
  logic (`OpenTaan.Game`), each checked against the reference sessions.
- **The rest of the admin API.** Sessions, rooms, characters, match history,
  moderation and node operations, each with its own permission.
- **Observability.** OpenTelemetry from both servers into Prometheus and Grafana, as
  an optional compose profile.
- **Double-entry ledger** for in-game currency, with a daily reconciliation job.
- **Security hardening** from the [threat model](security/threat-model.md)'s open
  risks: a least-privilege database role per service, automatic copies of the
  audit chain head outside the database, MFA for operators, and recording
  refused attempts.

## Changes along the way

| Change | Why |
|---|---|
| Approved adjustments go to an outbox instead of being applied directly | Gold lives in the game server's state, which does not exist yet; the outbox keeps approvals safe until it does |
| No users in the Keycloak realm; operators are added by script | No credentials in the repository |
| The transport test project waits for the transport | The .NET 10 test runner counts a project with no tests as a failure |
| Reading an account is audited, not only changes | An account holds a player's email; who looked at personal data matters as much as who changed it |
| The database builds the hash chain; the admin server only checks it | The game server will write to the same trail; with the rules in the schema, neither service can get them wrong |
| Packet codecs came before CodeQL | CodeQL waits on GitHub code scanning (next row); the codecs had nothing to wait for |
| CodeQL comes after the other scanners | It reports through GitHub code scanning, which this repository does not have yet; Trivy and Dependabot work without it |
