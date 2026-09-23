# Game server

.NET 10 TCP server for the TAAN game client. Clients connect on port 2180.

## Projects

| Project | What it holds |
|---|---|
| `src/OpenTaan.Transport` | Framing, encryption and handshake keys. No game logic, no database, no hosting. |
| `src/OpenTaan.Protocol` | Packet codecs: the bytes inside a frame, read and written by hand. No framing, no game logic, no database. |
| `src/OpenTaan.GameServer` | The executable: configuration, the TCP listener, logging. |
| `tests/OpenTaan.Transport.Tests` | Framing, the packet cipher, the seed schedule and handshake keys, including hostile input. |
| `tests/OpenTaan.Protocol.Tests` | Each codec's bytes, built from invented values, and hostile bodies: forged counts, broken text, leftover bytes. |
| `tests/OpenTaan.Conformance.Tests` | The reference sessions and the declared layouts. Skips when the contract is absent. |
| `tests/OpenTaan.Architecture.Tests` | Dependency rules between projects, enforced as tests. |

More projects are added as the port reaches them: `OpenTaan.Persistence`
(PostgreSQL) and `OpenTaan.Game` (handlers and game state).

## Packet codecs

Each packet is a record with a `TryRead` for bodies and a `Write`. Reading never
throws on bad input and never trusts a length: a body that is short, carries the
wrong packet, declares more than it holds, or has bytes left over is refused with
a `PacketError`. Credentials are held in `Secret`, which prints as `***`, so a
logged packet does not carry its password with it.

Each packet names itself once, as the client does, in `[WireClass("…")]`; the
name may contain underscores that its C# name drops. The registry id that leads
its bytes is computed from that name by `RegistryIds.Of`, never written down.

Codecs are added one stage of the protocol at a time and written by hand, not
generated from the reference data, so only the layouts this server implements
appear here. So far: the handshake, and the account and login packets.

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
