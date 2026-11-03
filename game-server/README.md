# Game server

.NET 10 TCP server for the TAAN game client. Clients connect on port 2180.

## Projects

| Project | What it holds |
|---|---|
| `src/OpenTaan.Transport` | Framing, encryption and handshake keys. No game logic, no database, no hosting. |
| `src/OpenTaan.Protocol` | Packet codecs: the bytes inside a frame, read and written by hand. No framing, no game logic, no database. |
| `src/OpenTaan.GameServer` | The executable: the listener, one session per connection, the packet dispatcher, the login handlers, and accounts held in memory until the database stage. |
| `tests/OpenTaan.Transport.Tests` | Framing, the packet cipher, the seed schedule and handshake keys, including hostile input. |
| `tests/OpenTaan.Protocol.Tests` | Each codec's bytes, built from invented values, and hostile bodies: forged counts, broken text, leftover bytes. |
| `tests/OpenTaan.GameServer.Tests` | Sessions on real loopback sockets: the login flows, and every way a connection is refused or closed. |
| `tests/OpenTaan.Conformance.Tests` | The reference sessions and the declared layouts, and the sessions played against real connections. Skips when the contract is absent. |
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
appear here. So far: the handshake, the account and login packets, the
character packets for creation, name checks, selection and deletion, and the
packets for cash, the channel list, channel moves, entering and leaving the
lobby, and the picture the client sends of a character.

Some packets carry data in the client's envelope (`DataEnvelope`): a signature, a
checksum, and contents that may be compressed, scrambled with a key, or both. The
channel list is one, holding a tree of nodes shaped like XML (`DataNode`). Every
form is read, because the client compresses what it builds when that saves space;
the server writes envelopes uncompressed, as the client has always been sent them.
Reading takes a size limit and checks it against the declared size before
allocating anything, stops decompressing at that size, verifies the checksum, and
refuses a tree nested deeper than 32 levels.

## Connections

Every connection gets a session of its own, running as tasks rather than on a
thread: a read loop that answers requests one at a time and in order, and a
write loop that is the only code sending on the socket. A handler queues its
reply and never waits for the network, so a client that stops reading costs its
own connection and nobody else's.

The server speaks first, with the handshake, and every frame after it is
encrypted. What the session does with a packet is a table written out in
`PacketDispatcher`:

| The packet | What happens |
|---|---|
| Has a handler | Decoded and handled. A body that cannot be decoded closes the connection. |
| Is a request whose layout is not settled and whose contents are not needed (the first packet on the login connection) | Answered without being decoded. |
| Is sent without expecting an answer, and is of no use yet (the heartbeat, telemetry) | Dropped. |
| Anything else | Logged by name and the connection is closed. |

Closing is deliberate. The client never re-sends a request, and some of its
screens stay locked until their reply arrives, so a request left unanswered is
lost for good; a closed connection is something the player at least sees. For
the same reason a frame that cannot be read, a reply that would not fit the
client's receive window, and a client that stops reading all end the connection
instead of being skipped.

The client checks or creates an account on one connection, closes it, and logs
in on another, so accounts belong to the server rather than to a connection.

An account is live on one connection at a time. A login naming one account in
both of the client's slots, or an account another connection holds, is refused
as already in the game, and the client returns to its login form; the
connection already playing is left alone. An account is freed when its
connection closes. A client that vanishes without closing, after a power cut
say, holds its account until the idle timeout (three minutes by default) closes
its connection.

## Characters

An account has one character, as in the client, which offers to create one only
for an account without. Names follow the client's own rules, 4 to 16 characters
from its Latin and Vietnamese set, and two names that differ only in case are one
name. Checking that a name is free and taking it are one step, so of two players
creating the same name at once, one gets it.

The client stores the characters a creation or deletion answer carries at the
account slot the answer names, without checking it. A slot the connection does
not hold is never echoed: the answer goes to slot 0. A selection or deletion
naming a character the account does not own is refused, and since a selection's
answer cannot say no, that one closes the connection. A new character keeps only
the appearance parts the client's creator offers (`Characters:CreatorParts`), so
a modified client cannot give itself items or a partner at creation.

## Channels and the lobby

The channels are configured (`Channels`), all served by this server: a move to
one is answered as entered, and the player stays on the connection it already
has. The client lists them under one group, which it calls a server, and shows
how full each is; a full or closed channel is refused, and the client does not
try one it is told is either. Channels have no level range. Checking that a
channel has room and taking the place are one step, so two players cannot both
take its last place, and a place is freed when its connection closes.

The client is told an address for the channels, which it would connect to only
to move to a channel on another server. It is `Channels:Server:PublicAddress`
when set; otherwise the bind address when that names one; otherwise the address
of the network interface this machine sends from, which is right on a local
network but not behind a router or in a container. The address and how it was
chosen are logged at start-up.

A lobby entry goes to the channel the connection moved to, or the first one when
it moved nowhere. Other players and rooms come with the room stage, so a lobby is
shown empty. Cash is the account's, and starts at `Accounts:Start:Cash`; a cash
request for a slot the connection does not hold is answered for slot 0, as the
character requests are.

On entering the lobby the client sends a PNG picture of each selected character.
It is kept only when `Portraits:Folder` is set (Development sets `portraits`),
as one file per character named by its id; a picture of a character the
connection has not selected is dropped, and one that is not a PNG file is kept
apart with a `.bin` extension.

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

This runs in the Development environment, where any login creates the account
it names (`Accounts:RequireRegistration` is off). A real client can log in,
create and select a character, pick a channel and enter its lobby; rooms are the
next stage. The server refuses to start with registration off in any other
environment. Accounts and characters live in memory until the database stage.

Settings live in `appsettings.json` and can be overridden with environment
variables, for example `GameServer__Port=2181`:

| Setting | Default | What it does |
|---|---|---|
| `GameServer:Port`, `GameServer:BindAddress` | `2180`, `0.0.0.0` | Where clients connect. |
| `GameServer:MaxConnections`, `GameServer:MaxConnectionsPerAddress` | `1000`, `32` | Connections past either are closed at once. |
| `GameServer:IdleTimeout` | `00:03:00` | Silence before a connection is closed; the client sends a heartbeat every minute. |
| `GameServer:SendTimeout`, `GameServer:SendQueueCapacity` | `00:00:10`, `256` | How long a frame may take to leave, and how many replies may wait, before a client that stopped reading is closed. |
| `GameServer:PacketNamesFile` | none | A JSON file naming packets by id, so logs name packets this server does not handle. Development points it at the local reference data. |
| `Accounts:RequireRegistration` | `true` | Whether an account must be created, with its password, before it can log in. |
| `Accounts:GameMasters` | none | Addresses of the accounts that log in as game masters. |
| `Characters:Start` | gold and counters `0`, `ExperienceCeiling` `300` | What a new character starts with. Every character starts at level 1; `ExperienceCeiling` is where level 2 begins and must be above 0. |
| `Accounts:Start:Cash` | `0` | The cash a new account starts with. |
| `Channels:Server:Name`, `Channels:Server:Type` | `Alpha`, `1` | The group the channels are listed under. The client uses the type only to pick the group it shows first, by level. |
| `Channels:Server:PublicAddress`, `Channels:Server:PublicPort` | none, `GameServer:Port` | The IPv4 address and port the client is told the channels are at; see above for what is used when none is set. |
| `Channels:List` | `Channel 1` to `Channel 5`, room for 100 each | Each channel's `Id`, `Name`, `Capacity`, and optionally `Disabled` and `Mode` (`6`, ordinary; arena modes are not served yet). |
| `Portraits:Folder` | none | Where the pictures clients send of their characters are kept. A relative path is taken from the content root. |
| `Characters:CreatorParts` | `1, 3, 5, 8` | The appearance parts the client's character creator offers; a new character keeps only these. |
