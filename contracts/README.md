# Contracts

Everything two implementations have to agree on, kept in one place and in
language-neutral formats. Code is generated from these files or tested against
them; it never redefines them.

## `openapi/`: the admin API

`admin-api.yaml` defines the admin server's HTTP API. It is written before the
code: the admin server's build generates Java interfaces from it and its
controllers implement them, so a change to the API starts as a change to this
file. Each operation names the permission it requires in `x-permission`.

## The game protocol

The packet layouts and reference sessions the game server is checked against
are derived from the game client's own files, so they are not committed here.
They are supplied locally, under `contracts/wire/`, which is ignored. Two rules
apply to anything that reads them:

- **Compare bytes, not packet types.** Two different layouts can share a packet
  name, so a test that only checks which packet was sent can pass while the
  bytes are wrong.
- **Little-endian throughout.** A 32-bit value carried as two 16-bit halves is
  low half first. Getting this backwards survives round-trip tests; only a byte
  comparison catches it.

## Coming next

- `control-channel/`: the frame format and command schemas the admin server
  uses to reach the game server.
