# 3. Contracts are written before the code, in language-neutral files

- Status: Accepted
- Date: 2026-09-22

## Context

Two services in two languages have to agree on several things: the game
protocol (with the client, and with any tool that reads captured traffic), the
admin API (with its future portal and any script an operator writes), and the
control channel between the two servers. The game protocol has no published
specification, and a wrong byte does not produce an error: the client hangs or
quits.

## Decision

Everything two implementations must agree on lives in `contracts/`, in
formats neither language owns. The contract is written or changed first, and
the code is generated from it or tested against it.

- **Admin API:** `contracts/openapi/admin-api.yaml`. The admin server's build
  generates Java interfaces from it. Controllers implement those interfaces,
  so a route, parameter or response that differs from the contract does not
  compile. Each operation names the one permission it requires in
  `x-permission`. Swagger UI serves the contract file itself.
- **Game protocol:** packet layouts and reference sessions. These are derived
  from the game client's own files, so they are supplied locally rather than
  committed (ADR 10). As the transport and packet codecs are built, the tests
  compare every byte the server sends with the reference sessions.
- **Control channel:** JSON Schemas in `contracts/control-channel/`, written
  before the channel is built.

## Alternatives considered

- **Code first, with OpenAPI generated from annotations.** Quicker to start,
  but the contract then records whatever the code happens to do, including its
  mistakes, and a reviewer cannot tell the intended API from the accidental
  one.
- **Defining the protocol in C# types.** The admin server and any analysis
  tool would then depend on the game server's code or on a copy of it.

## Consequences

- An API change starts as a diff to a YAML file that can be reviewed before
  any code exists.
- Validation declared in the contract, such as limits, patterns and required
  fields, is enforced by the generated code. Controller tests check that it
  answers with 400.
- The generated code must not depend on the code that implements it. An
  architecture test enforces this.
- The contract file is only as good as its review: generation guarantees the
  code matches the contract, not that the contract is right.
