# Game server

.NET 10 TCP server for the TAAN game client. Clients connect on port 2180.

## Projects

| Project | What it holds |
|---|---|
| `src/OpenTaan.Transport` | Framing and encryption. No game logic, no database, no hosting. |
| `src/OpenTaan.GameServer` | The executable: configuration, the TCP listener, logging. |
| `tests/OpenTaan.Transport.Tests` | Framing, the packet cipher and the seed schedule, including hostile input. |
| `tests/OpenTaan.Conformance.Tests` | The reference sessions and the declared layouts. Skips when the contract is absent. |
| `tests/OpenTaan.Architecture.Tests` | Dependency rules between projects, enforced as tests. |

More projects are added as the port reaches them: `OpenTaan.Protocol` (packet
codecs), `OpenTaan.Persistence` (PostgreSQL) and `OpenTaan.Game` (handlers and
game state).

## Protocol reference data

The packet layouts and reference sessions this server is checked against are
derived from the game client's own files, so they are not committed. Supply
them locally under `contracts/wire/` and the conformance tests pick them up from
there; without them those tests skip, naming what they would have checked, and
the rest of the suite runs as usual.

## Build and test

```sh
dotnet test --solution OpenTaan.GameServer.slnx
```

## Run

```sh
dotnet run --project src/OpenTaan.GameServer
```

Settings live under `GameServer` in `appsettings.json` and can be overridden
with environment variables, for example `GameServer__Port=2181`.
