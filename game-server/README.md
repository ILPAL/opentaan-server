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
lobby, the lobby's player list and its changes, the picture the client sends
of a character, the reports it sends while it plays the tutorial, the play time
it is allowed, and the room packets: creating, entering, quick-joining and
leaving a room, the room as a player is sent it, with its slots and its host,
the changes a room is told of, the lobby's room list and the players in a room,
what players do in a room: being ready, changing team and weapon, the host's
options, opening and closing slots, and expelling a player, and a match, a
mission's or one between teams: starting it, how each character starts, loading it,
a mission's levels, its turns (a shot
and every kind of step in it, a shot played back, the end and start of a turn, a
pass, a walk, a place, a fall and a ghost), its items and ghosts (an item used, the
items a shot spent, an item thrown away, an item in use taken off, a box caught, a
bonus, and where a ghost is sent), giving up, a player leaving it, its end and the
result screen's cards.

A room travels two ways: as a record, which the client sends to create a room
and is sent with each room it enters, and field by field in the lobby's room
list, where a mask names the fields present and they come in an order of their
own. Every room has eight slots, sent all together and in order, because the
client finds some of them by their place in the list; a slot index or a team the
client would use as an index without checking can be neither read nor written,
and the same holds for the slots a team change moves and a slot opened or closed.
A request the server answers even when it refuses it, the host's options, is read
whatever values it carries, so that a refusal is answered rather than closing the
connection.
Each request to create, enter or quick-join a room ends with the client's report
on its own code, which is read and kept but not checked: checking it needs data
from the client's own files.

A shot is read in full, every step of every kind in both of its lists, and written
back exactly as it came, because the server passes it to the other clients unchanged:
a body the codec cannot read is refused rather than passed on to clients that would
read it unchecked. A shot's slots are read as they come, since a mission's enemies
sit in slots of 100 and more; every packet that names a player's character refuses
a slot outside the room.

The item packets are passed on unchanged too, so they are read in full and written
back as they came. A box caught and a bonus each carry the catch as a step of the
catching kind, and the end of a turn, a box caught and a bonus read a change to a
team's gauge the same way. The client looks up a team's item lists by the team an
item packet names without checking it, so that team is one of the three team
numbers, or 0xFF where the client skips a team it is not given.

A match's result and its result screen's cards always carry eight entries, one for
each slot, because the client reads eight of each without checking how many came.
A start lists one entry for each character, no two for the same slot, since the
client looks them up by slot. A result card is a class of its own for each kind of
reward, and also carries its kind as a number, which the codec derives from the
class instead of holding it twice.

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
it moved nowhere. The player is listed everyone in that lobby, itself included,
and everyone already there is told it came in; when it leaves, by leaving the
lobby, by moving to a channel or by its connection closing, everyone left is told
it went. The client adds a player for every add and removes one for every removal
without checking what it shows, so each player is added once and removed once,
and listing a lobby, adding a player and telling the others are one step under
one lock. A lobby too large for one frame is listed in as many frames as it
needs. The lobby also lists its channel's rooms; see the next section.

Cash is the account's, and starts at `Accounts:Start:Cash`; a cash request for a
slot the connection does not hold is answered for slot 0, as the character
requests are.

On entering the lobby the client sends a PNG picture of each selected character.
It is kept only when `Portraits:Folder` is set (Development sets `portraits`),
as one file per character named by its id; a picture of a character the
connection has not selected is dropped, and one that is not a PNG file is kept
apart with a `.bin` extension.

## Rooms

From its channel's lobby a player creates a room, enters one, has one picked for
it, or leaves; a room belongs to one channel. Every room has eight slots on two
teams, the left two columns of the client's room screen against the right two. A
connection plays one character per account it logged in with, one or two, and its
characters always sit together on one team. Every slot they take carries the one
player id the connection's login was answered with, since the client tells its own
slots, and the host's, by that id.

- **Creating:** the request is checked as the client's own create dialog checks it
  (a name, a password when one is asked for, a game mode the room's type allows),
  and the server sets the rest: the status, the player counts and the map. The host
  takes the first slots of the first team. A mission room keeps the second team's
  slots closed: that side is the mission's own enemies, which every client plays
  from its own copy of the mission. A channel
  holds at most `Rooms:MaxPerChannel` rooms. Room numbers rise from 1 and a freed
  one is not given to the next room at once.
- **Entering:** the player sits on the team with fewer characters when all of its
  characters fit there, otherwise on the other team. When neither team can take
  them all, it is refused: as full when the room has fewer free slots than it has
  characters, and for lack of a slot when they are free but split between the
  teams. A wrong password, a room in another channel, and a player already in a
  room are refused with the client's own reasons.
- **Quick join:** the fullest waiting room in the channel that has no password,
  plays no mission and can seat all of the player's characters together, and of
  those the lowest numbered.
- **Leaving:** leaving, or the connection closing for any reason, frees the
  player's slots, and everyone left is told. When the host goes, the player in the
  lowest slot hosts, and the room is told that player is not ready before it is told
  who hosts. The last player out closes the room. A lobby entry or channel move
  from a room leaves the room first.

The client sends none of the four requests while one is unanswered, so every one
is answered exactly once, refusals included. The lobby is listed its rooms on
entry, never with their passwords, and is told of every room that opens, changes
or closes. One lock covers every room, so two players cannot both take a room's
last slot, and a player entering a lobby as a room opens sees it exactly once.

### In a room

While a room waits for its match, its players act on it, and everyone in the room
is told of each change, the player acting included. The client waits for an answer
to none of these requests but the host's options, so a request the rules below do
not allow is dropped, and logged. Each change is one step under the rooms' lock, so
everyone is told the same changes in the same order, and a player entering at that
moment sees each change exactly once: in the room it is sent, or after it.

- **Ready:** a guest is ready to start, or no longer, with all of its characters.
  The host starts the match instead, so it is never ready.
- **Team:** a player moves to the other team when that team has an open slot for
  each of its characters, taking the lowest ones; not in a mission room, whose
  players are all on one side, and not while ready.
- **Weapon:** a player picks the weapon each of its own characters takes into the
  match. Any weapon is kept as the client sends it, its random pick included: the
  weapons come from the client's own files, and the match decides what a random
  pick becomes.
- **Options:** the host changes the room's game mode, chances and map, or a mission's
  difficulty, within what the client's options dialog offers: in a room of teams a
  map the client lists for the channel's mode (`Rooms:MapsByChannelMode`), its
  random picks included, which stay as picked until the match decides them; in a
  mission room one of the missions the client lists (`Rooms:Missions`), at a
  difficulty from 0 to 2. The host's dialog waits
  for an answer, so a refused change is answered too, with the room as it is, to
  the host alone. The lobby is told of every change.
- **Slots:** the host closes an empty slot to players entering, or opens it again.
  Entering and quick join pass closed slots over, and the lobby's maximum for the
  room follows them. A mission room's other side stays closed.
- **Expel:** the host sends a player out. Everyone in the room, that player
  included, is told the player's id, by which each client finds its slots, and the
  player then leaves as it would on its own, all of its characters at once. Nothing
  stops it coming back.

The slots keep every change, so a player entering later is shown who is ready, on
which team, with which weapon.

A room's game mode sets the rules its matches are played by. The client sends it as
a number, and the server's code names each one (`GameMode`):

| Mode | Name | Rules |
|---|---|---|
| `0` | `Items` | Items in play. |
| `1` | `NoItems` | No items. |
| `2` | `CaptainItems` | Items in play, and each team has a captain. |
| `3` | `CaptainNoItems` | No items, and each team has a captain. |

A mission room plays `Items` alone, the only mode the client's mode picker offers
it. A room of teams may have any of the four, and its matches are played in `Items`
and `NoItems` for now: matches with captains come later.

## Matches

A match, a mission's or one between teams, is played from its start to its result
screen, turn by turn. The clients play the game: the shooter's client works a shot out and plays
it, every other client plays it back from what it sends, and each client walks and
places its own characters and reports their deaths. The server keeps the turns:
whose turn it is, what the turn's character may do, when the turn ends and who has
the next, and when the match is over. What a client tells of how its own characters
play goes to every other client in the match as it came, and never back to its
sender: a client sends on any shot, pass or fall it is given that names one of its
own slots, so a copy back would come round again. Items go the other way, as below.
Matches with captains and missions whose enemies fight back come next.

- **Starting:** the host starts once every guest is ready. Everyone in the room is
  told how each character starts, and only the characters. A random weapon is
  played as the dragon. The lobby shows the room playing, and no one enters it until
  it waits again. A start the rules do not allow is not answered: the client waits
  on none.
  - A mission room's host alone may start. The mission is laid out from a seed drawn
    for this match, from which every client lays it out alike. A mission the client
    might not have is never started: it would leave every client in the room without
    a map, which is why the host's options take only `Rooms:Missions`.
  - A match between teams starts only between teams of the same size, as the host's
    client checks itself, and not yet with captains: in `CaptainItems` and
    `CaptainNoItems` (modes 2 and 3) the client asks for the captains first, which
    comes later. It is played on the room's map.
    The client cannot load one of its own random picks, `200` to `203`, so a room on
    one is played on a map drawn evenly at every start from the pick's group among
    the maps of the channel's mode (`Rooms:RandomMaps`) and the extras every pick
    may draw (`Rooms:RandomMapExtras`). The room still shows the pick, and its next
    match draws again.
- **Loading:** a client reports a match between teams loaded once its camera has
  toured every character, two seconds each, and a turn reaching it before that sets
  its tour going again over the turn. So the match's first turn waits until every
  client has reported, but no longer than `Matches:LoadWait` once the first has,
  since every client tours the same characters at the same pace, and no longer than
  `Matches:LongestLoad` from the start in all. It goes to the lowest character still
  in the match. A report counts for its client whatever slots it names, since only
  the client knows it has loaded; where it says characters stand is taken only when
  they are its own. A mission's first turn waits for its first level instead.
- **Levels:** every client asks for each level: the first once it has loaded, each
  next one as it finishes playing back the shot that brings its own count of kills
  to the level's target, and one past the last once the mission is cleared. Only
  the next level begins, once, and only while the last level runs is the mission
  cleared: the server cannot count the kills, which happen in each client's play of
  a shot, but it holds every client to the order and the number of the levels. A
  later level and the clear are taken only while a shot is played, and held until
  every client has played it back: announced at once, the level's opening would
  start on a client still in the shot, and the end would cut its shot short. A
  level begins with an opening of about two and a half seconds on every client,
  which places its enemies, and no end or start of a turn goes with it: the client
  changes level by itself, and either would break that. Its first turn waits until
  every client in the match reports the opening over, since a turn that reaches a
  client still in it would leave that client without the level's enemies, or for
  `Matches:LevelReadyWait` at most, so one stuck client cannot hold up the rest. The
  first level's goes to the lowest character still in the match, a later level's to
  the next character after the one whose shot finished the level before.
- **Turns:** the teams take turns, each going through its characters still in the
  match in slot order, round again from its lowest: two against two in slots 0 and 1
  against 2 and 3 play 0, 2, 1, 3. A team with nobody left in play is passed over,
  so a mission's players, all on one team, go in slot order. The turn's character may walk and be
  placed, which goes to the others, then shoot once or pass. A shot is taken only
  for the turn's character, from its own client: any other would play on every other
  screen while no turn was its. It goes to the others, and the turn ends once every
  client in the room has reported it played back, since the start of a turn does not
  wait for a shot a client is still playing. Each client reports a shot twice, and
  is counted once. The others are given `Matches:ShotWait` once the first has
  finished, and a shot no client reports ends `Matches:LongestShot` after it
  arrived. A pass goes to the others and the turn ends at once. The client passes
  for its player after a turn of 15.9 seconds; a turn with neither a shot nor a pass
  within `Matches:TurnWait` is passed by the server, for a client that has stopped
  answering. A turn ends with the end of the turn, then the start of the next
  character's, both to everyone. In a mission, the start of every turn also carries
  the level's turn count, which the client shows against the level's turn limit: it
  goes up by one with every shot a player fires, a pass adds nothing, and every level
  starts again from none.
- **Deaths:** a character that dies in a shot is reported by its own client as it
  plays the shot back, before it reports the shot played back; one that falls while
  walking is reported by its own client, and the fall goes to the others. Everyone
  is told the character is a ghost of its team, once, where its client last
  reported it standing: once loaded, walking, placed or passing. A turn whose character is out
  of the match passes on at once, but during a shot nothing is decided until every
  client has played it back, since the shot may kill more.
- **Items:** the clients decide every item, for now. The start of every turn tells
  them how many item boxes the turn's shot may drop, `Matches:ItemBoxesPerTurn`, or
  none in a match without items (`NoItems` or `CaptainNoItems`, modes 1 and 3), and how good the items are, `Matches:ItemDropQuality`; the shooter's client places the
  boxes along its missiles' paths, and the client that catches one, or whose
  character is given a bonus at the end of its turn, draws the item. A box a missile
  catches is a step of its shot; a box a ghost catches is sent on its own. A client
  acts on an item used, the items a shot spent, an item thrown away or taken off, a
  box its ghost caught and a bonus only when the server's copy comes back, and gives
  no item of its own accord, so each goes to everyone in the match, its sender
  included, as it came. Where an item went goes back to its sender alone, as in the
  recorded sessions. Each is passed on only for one of the sender's own characters,
  the only ones the client sends them for, whoever has the turn and during a shot
  too, since a step of a shot can have a client use an item as it plays the shot
  back. Deciding items on the server comes later, as its own step.
- **Ghosts:** the player of a dead character may send its ghost somewhere, which goes
  to everyone in the match, its sender included. The start of every turn after that
  moves the ghost from where its client last reported it to where it was sent, until
  its client reports it there; where a ghost stopped goes to everyone else. Only a
  client's own dead character has a ghost to send or report. A player who leaves
  takes its ghost off every field, and no turn moves it again.
- **Giving up and leaving:** the client lets a player give up once a match has run
  for two minutes, for all of its characters at once; the player stays and sees
  the match end. A player leaving, or its connection closing, is taken off everyone
  else's field, then out of the room as it would be from a waiting room, and is no
  longer waited for. Either way a turn that was theirs passes on. A team whose
  every character has given up, left or died loses. The other clients are not told
  of a give-up, since the client does nothing with one: the character stays on
  their fields, and its turns pass on at once.
- **The end:** a cleared mission is won, even by a shot that also killed the last
  player, and a lost one is the mission's. A match between teams is won by the
  other team once one has nobody left in play, and when one shot takes the last of
  both teams out, by the team whose character fired it: the client shows no draw. A
  shot's outcome is decided once every client has played it back. Everyone still
  in the room is sent the result screen's cards, then the result, which builds the
  screen from the cards, so the cards always come first. Nothing about the match's
  turns is answered once it is over, since the client would act on it and leave its
  result screen. Every guest is no longer ready, as the client shows by itself.
  Rewards that last come later, so a card shows no gold and grants none, and the
  wins and losses the client adds on its own screen are not stored yet.
- **The cards:** each character turns over one card of the first eight, which then
  belongs to no one else; everyone sees each one turned. A card the player picked
  that another took first is refused to that player alone, who may pick again; a
  card picked for a player whose time ran out is one of those left. The room waits
  for players again once every character in it has its card, or
  `Matches:ResultWait` after the end, by when every client has left its result
  screen by itself.

A match lives under the rooms' lock, so a level begun, a shot, a death, a player
leaving and the end happen in one order for everyone. Its waits run as timers on
the server's clock, one at a time, each armed before anyone hears of what it waits
on, and a timer that fires for a match that has moved on finds nothing to do.

## Play time

Some regional builds of the client, the Vietnamese one among them, limit how long
a player may play. As such a client builds a lobby it checks every character slot,
and quits, after a notice, for any slot that has played as long as it is allowed;
it knows neither number until the server says, and both start at 0, so told
nothing it quits at its first lobby. So every channel move that is entered is
preceded by the play time: the limit for each account slot, then the played time
for each selected character, with the client counting it from then on, then a
reset to 0. It goes ahead of the move's answer because the client builds the
lobby before it asks to enter it. The limit is `PlayTime:AllowedSeconds`, none by
default. The client keeps the count and enforces it; this server does not store
played time yet, so the count starts again at every channel entry. A client
counting play time shows in chat, from the first hour, how long is left.

## The tutorial

The client plays the tutorial by itself, before the first lobby, for a character
below level 2 whose selection says it has not finished it. It waits on nothing
from the server, but it still reports some of what happens: an item picked up,
where its ghost stopped, and the tutorial's end. The first two are a match's
reports, and from a client in no match they change nothing and are logged. The last is recorded
against the character selected in the connection's first slot, never the one the
report names, which the client fills with the first character it selected since
it started. The client keeps no record of its own, so this record is all that
stops the tutorial coming back at every selection. Nothing answers the report,
and a repeat changes nothing.

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

From this folder (`game-server/`), with the .NET SDK named in `global.json`:

```sh
dotnet run --project src/OpenTaan.GameServer
```

From the repository root, the project is `game-server/src/OpenTaan.GameServer`.
The server is up when it logs `Listening for game clients on 0.0.0.0:2180`. Just
before that it logs the address it gives clients for the channels, for example
`Clients are told the channels are at 192.168.1.20:2180 (Detected)`. Point the
client at that address. Stop the server with Ctrl+C.

`dotnet run` picks up `Properties/launchSettings.json`, which sets
`DOTNET_ENVIRONMENT=Development` and with it `appsettings.Development.json`.
Without the launch profile (`--no-launch-profile`, the published
`OpenTaan.GameServer.dll`, or the Docker image), the server runs in Production,
where registration is required and unhandled packets close the connection. The
content root is the project folder, so in Development the portraits clients send
are kept in `src/OpenTaan.GameServer/portraits/`.

This runs in the Development environment, where any login creates the account
it names (`Accounts:RequireRegistration` is off). A real client can log in,
create and select a character, pick a channel, enter its lobby, create, enter and
leave rooms, do everything a waiting room offers, start a mission or a match
between two teams of the same size and play it turn by turn: walk, shoot, pass,
die, see each level begin, clear the mission or lose it, win or lose against the
other team, catch, use and throw away items, send its ghost about, give up or
leave, and see its result screen and pick a card. Matches with captains come
later. The server
refuses to start with registration off in any other environment. Accounts,
characters and rooms live in memory until the database stage.

Settings live in `appsettings.json`. You can override them with environment
variables, or on the command line after `--`:

```sh
GameServer__Port=2181 dotnet run --project src/OpenTaan.GameServer
dotnet run --project src/OpenTaan.GameServer -- --GameServer:Port=2181 --PlayTime:AllowedSeconds=3600
```

| Setting | Default | What it does |
|---|---|---|
| `GameServer:Port`, `GameServer:BindAddress` | `2180`, `0.0.0.0` | Where clients connect. |
| `GameServer:MaxConnections`, `GameServer:MaxConnectionsPerAddress` | `1000`, `32` | Connections past either are closed at once. |
| `GameServer:IdleTimeout` | `00:03:00` | Silence before a connection is closed; the client sends a heartbeat every minute. |
| `GameServer:SendTimeout`, `GameServer:SendQueueCapacity` | `00:00:10`, `256` | How long a frame may take to leave, and how many replies may wait, before a client that stopped reading is closed. |
| `GameServer:PacketNamesFile` | none | A JSON file naming packets by id, so logs name packets this server does not handle. Development points it at the local reference data. |
| `GameServer:DropUnhandledPackets` | `false` (Development: `true`) | Logs and drops a packet this server has no handler for instead of closing the connection, so one run of a real client shows everything a screen sends. Allowed only in Development: a request dropped this way is never answered. |
| `Accounts:RequireRegistration` | `true` | Whether an account must be created, with its password, before it can log in. |
| `Accounts:GameMasters` | none | Addresses of the accounts that log in as game masters. |
| `Characters:Start` | gold and counters `0`, `ExperienceCeiling` `300` | What a new character starts with. Every character starts at level 1; `ExperienceCeiling` is where level 2 begins and must be above 0. |
| `Accounts:Start:Cash` | `0` | The cash a new account starts with. |
| `Channels:Server:Name`, `Channels:Server:Type` | `Alpha`, `1` | The group the channels are listed under. The client uses the type only to pick the group it shows first, by level. |
| `Channels:Server:PublicAddress`, `Channels:Server:PublicPort` | none, `GameServer:Port` | The IPv4 address and port the client is told the channels are at; see above for what is used when none is set. |
| `Channels:List` | `Channel 1` to `Channel 5`, room for 100 each | Each channel's `Id`, `Name`, `Capacity`, and optionally `Disabled` and `Mode` (`6`, ordinary; arena modes are not served yet). |
| `PlayTime:AllowedSeconds` | none | How long a player may play, in seconds, as the client is told on entering a channel; none means no limit. Must be above 0: with no time allowed, the client quits at every lobby. |
| `Rooms:MaxPerChannel` | `500` | Rooms one channel may hold at once; a request to create one more is refused. |
| `Rooms:MaxNameLength` | `128` | The longest room name, in UTF-16 code units, a request to create a room may carry. At most 1000, so every room reply fits a frame. |
| `Rooms:MapsByChannelMode` | mode `6`: maps `1` to `21` and the random picks `200` to `203` | The maps the host of a room of teams may pick, by the mode of the room's channel: those the client's own map list offers in that mode. Every mode `Channels:List` uses needs a list, or the server refuses to start. |
| `Rooms:RandomMaps` | `200`: maps `1` to `21`; `201`: `7, 8, 12, 14, 19, 20`; `202`: maps `1` to `21` but `3, 6, 12, 15`; `203`: `3, 6, 12, 13, 15, 21` | For each of the client's random picks, its group of maps, as the client's own map table has them: a match on the pick is played on a map drawn evenly from the group's maps that the channel's mode offers, and the extras. The server refuses to start if a random pick a mode offers has no map to draw from there, if a map that is no random pick has a group, or if a group or the extras hold a random pick. |
| `Rooms:RandomMapExtras` | `300, 500` | Maps every random pick may also be played on: maps the client can load but never offers a host. |
| `Rooms:Missions` | mission `0`, of `4` levels | The missions a mission room may play, by number, each with its number of levels: those the client's own mission list offers. Must hold mission `0`, which every new mission room plays until its host picks another, and give each mission 1 to 254 levels. |
| `Matches:LevelReadyWait` | `00:00:10` | How long a mission level's first turn waits for every client to report the level's opening over. |
| `Matches:LoadWait` | `00:00:05` | How long the first turn of a match between teams waits, once the first client has reported the match loaded, for every other client to report it too. |
| `Matches:LongestLoad` | `00:00:30` | How long the first turn of a match between teams may wait in all, from the start, in case no client reports the match loaded. Must be longer than the client's tour of a full room's characters, 16 seconds, or the server refuses to start: every full match would start before any client had loaded it. |
| `Matches:TurnWait` | `00:00:25` | How long a turn waits for its character to shoot or pass before the server passes it. Must be longer than the client's own turn of 15.9 seconds, after which it passes for its player, or the server refuses to start: a turn cut while its client still counts would leave a last-second shot on its shooter's screen alone. |
| `Matches:ShotWait` | `00:00:05` | How long a shot waits, once the first client has played it back, for every other client to finish it. |
| `Matches:LongestShot` | `00:01:00` | How long a shot may wait in all, from its arrival, in case no client reports it played back. |
| `Matches:ResultWait` | `00:00:25` | How long after a match's end its room waits for every player's result card before it waits for players again. |
| `Matches:ItemBoxesPerTurn` | `1` | How many item boxes each turn's shot may drop, as the start of the turn tells every client; the shooter's client places them. `0` lets none drop, and none drop in a match without items (`NoItems` or `CaptainNoItems`, modes 1 and 3). |
| `Matches:ItemDropQuality` | `20` | How good the items the clients draw are, from `0` to `100`, as the start of every turn tells them: higher makes the rarer items likelier. Above `100` the server refuses to start, since the client would hold it to `100`. |
| `Portraits:Folder` | none | Where the pictures clients send of their characters are kept. A relative path is taken from the content root. |
| `Characters:CreatorParts` | `1, 3, 5, 8` | The appearance parts the client's character creator offers; a new character keeps only these. |
