# 2. .NET for the game server, Spring Boot for the back office

- Status: Accepted
- Date: 2026-09-22

## Context

The two services have different jobs.

- **The game server** keeps long-lived TCP connections, decodes and encodes a
  binary protocol that the client fixes byte for byte, and runs game state for
  many players at once. What matters is control over memory, bytes and threads,
  and predictable latency.
- **The admin server** is a back office that changes what players own. What
  matters is the set of conventions reviewers and auditors expect of such
  systems: a documented API, a standard identity provider, declarative security,
  transactions, and tests against a real database.

## Decision

Write the game server in .NET 10 and the admin server in Spring Boot 4 on
Java 25. They share contracts and the database schema, never code.

## Alternatives considered

- **Java for both.** Netty would serve the game server well, but .NET's
  `Span<T>`, `System.IO.Pipelines` and source-generated logging make
  allocation-free protocol code the default rather than an effort.
- **.NET for both.** ASP.NET Core is a capable back-office platform. Spring
  Boot, Spring Security and Spring Data are what most banks and consultancies
  build back offices on, and the admin server is meant to show that stack.
- **One process for both.** Game traffic and operator traffic have different
  security needs and different scaling needs. Running them in one process
  would mean the internet-facing game port shares memory with the operator API.

## Consequences

- Two toolchains, two dependency ecosystems and two CI workflows.
- Anything both services must agree on has to be written in a neutral format
  (see [0003](0003-contracts-before-code.md)), because neither service can import
  the other's types.
- A bug in the internet-facing game server cannot reach the operator API's
  memory, secrets or tokens.
