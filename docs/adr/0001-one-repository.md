# 1. One repository for both servers, the contracts, the database and the deployment

- Status: Accepted
- Date: 2026-09-22

## Context

OpenTaan has two services in two languages, a game server and an admin server.
They share one database and a set of contracts: the game protocol, the admin
API and, later, the control channel between them. A change often touches more
than one of these at once. Adding a column, for example, means a migration, the
code that uses it and sometimes the API that exposes it.

## Decision

Keep everything in one repository: `game-server/`, `admin-server/`,
`contracts/`, `database/`, `deploy/` and `docker-compose.yml`. Each service has
its own build and its own CI workflow, triggered only by the paths it depends
on. A third workflow starts the whole stack.

## Alternatives considered

- **One repository per service, with shared contracts published as packages.**
  This is the usual shape once teams own services separately. For one
  maintainer it would turn every cross-cutting change into several coordinated
  releases, and the contracts would need a versioning scheme before they had a
  second consumer.
- **Git submodules for the shared parts.** These have the same coordination
  cost, plus the submodule pointer as a new way to get out of step.

## Consequences

- A change and everything it depends on land in one pull request: a
  migration, the code that needs it, the contract and the stack test.
- CI runs only what a change can affect, so a documentation fix does not build
  both servers.
- Services must not reach into each other's folders. They share
  `contracts/` and `database/`, nothing else. The admin server's image is
  built from a context that holds only its own sources and
  `contracts/openapi`, and each server's architecture tests guard its own
  layering.
- If the services are ever owned by separate teams, the repository can be
  split along the same folders.
