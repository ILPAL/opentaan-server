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
- Sixty-four game connections claim one account at the same moment, released
  together by a barrier. Exactly one holds it, and every other is told which
  one. A version that checked and claimed under two locks failed this test.
- Twelve players enter one lobby at the same moment, and each must end up
  listing all twelve, every one exactly once. A version that listed the lobby and
  added the player without the roster's lock failed this test on every run.
- Two players race for a room's last slot, twenty times over on fresh servers.
  Each time one is seated and the other is refused as full, both are answered,
  and everyone already in the room is told of the one slot taken.
- A player enters a lobby while a room opens in it, twenty times over, and must
  see the room exactly once: in the list it is sent, or as a change after it.
- Eight players create rooms at once in a channel that holds three, and three
  rooms are created.
- A member of a room stops reading while others come and go. Once its send queue
  is full it is closed and leaves the room, and the room is told it left exactly
  once.
- A host sends a player out of a room as that player leaves on its own, twenty
  times over. The player's slot is freed once and the player is answered once,
  and the expulsion reaches both of them or, when the leave came first, neither.
- A host changes a room's options as a player enters it, twenty times over. The
  player is shown the new options exactly once: in the room it is sent, or as a
  change after it.
- Two clients in a mission ask for the same level at once, as every client does,
  twenty times over. Each is told once that the level begins.
- The last two players in a mission give up at once, twenty times over. The match
  ends once, and each is sent its end once.
- Two players pick the same result card at once, twenty times over. One turns it
  over, everyone is told so once, and the other is refused alone.
- Both clients in a mission report a shot played back at once, twice each as the
  client does, twenty times over. The turn passes once.
- Both clients ask for the next level as they finish playing back the same shot,
  twenty times over. The level begins once, after the shot.
- A client leaves as the shooter reports the shot played back, twenty times over.
  The turn passes once, and after the leave.
- Both clients in a match between teams report it loaded at once, twenty times
  over. Its first turn starts once.
- A player leaves a match between teams as the last other player reports it loaded,
  twenty times over. The first turn starts once, and after the leave.

Each race passes whichever way it goes, so a pass only means something if both
ways come up. Counted once over sixty rounds of each, both did: the expulsion
came first 56 times and the leave 4, the new options were in the room sent
55 times and came as a change 5 times, and of two players picking the same result
card, the host's pick came first 54 times and the guest's 6.

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

The game server keeps accounts both in memory, for its fast tests, and in
PostgreSQL, where they last. The hundreds of flow tests run on the first, so they
speak for the second only if the two behave alike. One contract of tests runs
against both stores, including creations that race for one address and reads over
new connections, as after a restart; a store that passes it for one and fails it
for the other has drifted.

### 6. The real database, the real migrations

Integration tests start PostgreSQL 18 in Testcontainers and apply the files in
`database/migrations`, the same ones Flyway applies in the stack. No schema is
generated for the tests, so a mapping that disagrees with a migration fails
the test run the way it would fail a deployment.

The game server's storage tests do the same with Flyway itself, in the image the
stack runs, and give every test a database of its own copied from the migrated
one. They also check what only the database can refuse, such as an account with
no gender, and the startup check that stops the server on a schema older than it
needs. Where no Docker runs they can use a PostgreSQL named for them instead; with
neither they skip, except in CI, where they fail, since database tests that skip
there pass without having checked anything.

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
  waited for the transport instead of existing empty.
- **A checksum nothing ever checked.** The frame checksum is accumulated inside
  the transformation, over whatever was fed to it. Because the transformation is
  its own inverse, that means plaintext when encrypting and ciphertext when
  decrypting, and the two agree only for an even number of whole key blocks.
  Verification would have rejected almost every frame -- but every caller had
  switched it off, so it never ran and the disagreement stayed hidden. Both
  directions now checksum the plaintext, and the tests use the body lengths that
  tell the two apart.
- **A handshake that could switch encryption off.** The server's first frame
  carries the values both ends derive the session seed from, and a seed of zero
  tells the client the connection is unencrypted. The obvious guard is to re-roll
  values that derive zero a few times. But a retry loop that keeps its last roll
  when it runs out turns a broken random source into a session with no
  encryption, and nothing reports it. `HandshakeKeys` cannot be constructed from
  values that derive zero, and generation that keeps failing throws instead of
  settling for the last roll. A scripted random source that only produces such
  values proves it.
- **Seeds one client could predict for another.** The handshake values travel
  in the clear, so a better generator does not hide any single connection's
  traffic; the client's cipher cannot do that (threat model T-G4). A general
  purpose generator is a different problem: its internal state can be rebuilt
  from a few hundred of its outputs, and every connection shows its handshake
  to its own client. The values come from the operating system's cryptographic
  generator, and the shift is drawn without the modulo bias that would favour
  small values.
- **A unit test that could not fail.** The layout test for the check-account
  answer used status 1 and slot 1. Both are single bytes, so writing them the
  wrong way round produced the same body and the test stayed green. Planting
  exactly that bug found it; the test now uses two different values. The same
  planted bug also failed the conformance check that reads recorded bodies from
  the declared layout, which is why that check exists.
- **A connection that kept talking after it stopped understanding.** Every frame
  is read with a seed both ends move on in step. A reader that meets a length no
  client could send, throws its buffer away and carries on has lost that step for
  good: each later frame is decrypted with the wrong seed and misread, while the
  connection looks alive. Such a frame now closes the connection, and so does a
  frame whose checksum does not match.
- **A request dropped in silence is a client that hangs.** The client never
  re-sends a request, and several of its screens stay locked until their reply
  arrives. Skipping an unknown packet, or one whose body cannot be read, would
  leave the player on a screen that never recovers, with nothing in the log
  worth reading. Every such case closes the connection, and the log names the
  packet.
- **A server setting the plan had left for later.** Playing the recorded
  sessions against real connections found one login answered with a different
  account type from all the others: the session was recorded with that account
  configured as a game master, which opens the client's console. It was small,
  it comes only from configuration, and it is now `Accounts:GameMasters`, rather
  than an exception in the test.
- **A request no recording had.** The first real client to connect opened its
  login connection with a request that no recorded session contains, because the
  sessions were scripted rather than captured from play. The server closed the
  connection and logged the request by name, so the gap took one log line to
  find, where a dropped request would have left a client waiting in silence. Its
  answer is now built from the client's own reader. With no recording to replay,
  a body written from invented values is read back through the declared layout
  instead, and the check fails the day a recording turns up, so the packet then
  joins the rest.
- **A state table stricter than the client.** A table of which packet is allowed
  in which state is the obvious guard. The recorded sessions show the client
  checking a character name after a failed login, and sending telemetry and a
  heartbeat whenever it likes, so such a table, written ahead of real traffic,
  would close connections a real client holds. The session keeps one rule, a
  connection logs in once, and the rest waits until recorded traffic can
  justify it.
- **Recorded values that cannot tell fields apart.** A new character in the
  reference sessions has the same value in most of its counters, so two of them
  read and written the wrong way round replay perfectly and agree with the
  declared layout. The unit tests build every field of a character and its
  appearance from a different value, and a planted swap of two such counters
  fails them.
- **A planted bug that did not compile.** One planted bug left code unreachable,
  which this build treats as an error, so no test ran and nothing reported a
  failure. A planted bug proves something only when the build succeeds and a
  test then fails; the runs now report build errors next to test failures.
- **A bug in both directions at once.** The data envelope can scramble its
  payload with a keystream, and scrambling and unscrambling are the same
  operation. A keystream built wrong, by adding a step instead of subtracting
  it, scrambles and unscrambles consistently, so every round trip passes. Two
  envelopes built by an independent implementation from invented contents are
  now test vectors; the planted mistake fails both and nothing else.
- **A recorded placeholder the server should not copy.** The reference sessions
  give every new character's appearance a gender of 0, where the client builds
  its own with the player's gender and draws a character's default parts by it.
  This server sends the account's gender. Rather than loosen the session replay,
  the one field is adjusted in the recording before comparing: the recorded
  answer is decoded, the field set, and the answer encoded again, so every other
  byte is still compared as recorded. A planted run without the adjustment fails.
- **A recorded level range nobody applied.** The reference sessions list every
  channel for levels 50 to 100, and then let level 1 characters into one. This
  server lists no range, since it applies none, and the session replay adjusts
  the recorded list the way it adjusts the gender above. A planted range fails
  the replay and a unit test.
- **A field no test could see.** A planted channel move that described every
  channel as an ordinary one passed: every channel in the tests was ordinary. A
  closed channel of another kind is now described in a test.
- **A restored file older than its bug.** Planted bugs are undone by restoring
  the file from a copy, which leaves it older than the build that holds the bug,
  so an incremental build kept the planted code and later runs failed for a bug
  that was no longer in the source. Planted runs are now followed by a build
  from scratch.
- **A planted bug that never ran.** Warnings fail the build, and three of the
  first bugs planted in the lobby's player list left a parameter unused or code
  unreachable. Their runs failed to build, reported no failing test, and read as
  bugs the tests missed. The runs now tell a build that failed from tests that
  passed, and those bugs were planted again in a form that builds; each was
  caught.
- **A test that slowed every other test.** The first version of the concurrent
  claim test held sixty-four thread-pool threads on its barrier. The pool
  starved, the whole suite took ten times as long, and two tests with short
  timeouts failed, one of them an existing heartbeat test. The failures
  were in other tests; the cause was the new one. Its contenders now run on
  threads of their own.
- **Recorded values that cannot show a swap.** Every recorded request to create,
  enter or quick-join a room ends with the client's report on its own code, and
  every one of those reports is zeros. A planted codec that read two of the
  report's fields in each other's place, and wrote them back the same way, passed
  the replay and the declared layout; only the unit tests caught it. The layout is
  now also checked on those requests written from invented values, and the same
  plant fails it.
- **A comparison that could never succeed.** A character's appearance holds its
  sixteen parts as a list, and a record compares a list by reference, so an
  appearance read from bytes never equalled the one it was written from. Tests
  had compared appearances field by field around it. A room's slot holds an
  appearance and its tests compare whole slots, so the appearance now compares
  its parts one by one.
- **A test that failed one run in five.** The test of a room member that stops
  reading gave every connection a send queue of three, so that member would fill
  its queue soon. An ordinary channel move sends four packets at once, the play
  time and the answer, so about one run in five closed a healthy player before the
  test reached its point. The queue is now larger than anything the server sends
  at once, and the test runs more rounds instead.
- **A race the test could not see.** A planted lobby entry that listed the rooms
  under the room lock and joined the lobby only after letting it go passed the
  race test: the gap between the two is a few instructions, and twenty rounds
  never put a room's creation inside it. With the gap widened by a pause of five
  milliseconds the test failed. So a pass there bounds how often such a race
  shows, and proves nothing; the one lock taken around both steps is what rules
  it out.
- **An answer that came before the news.** The first version sent a player
  entering a room its room first, and told everyone else after. The order between
  connections means nothing to the client, but writing the lobby's race test
  showed that a test holding that answer, and checking that another player had
  nothing more on its way, could finish before that player was told, and pass
  over a change it should have counted. The player is now sent the room last, so
  once it has its answer everyone else has been told, for tests and players alike.
- **A test that read one player before another had acted.** The first version of
  the race between a host's options and a player entering read everything the
  entering player had been sent, then the host's. Two connections are not ordered
  with each other, so the entering player could be answered before the host's
  change had even been handled, and the test failed on a correct server. It now
  waits for the host's own answer first: by then the change has been handled, and
  anything it sent the entering player is queued ahead of that player's next
  answer.
- **A recorded answer a real client ignores.** The recorded expulsion carries the
  expelled character's id. The client looks for the slots by the player's id,
  which is a different number, so a real client sent that message does not react
  to it, and the player sent out is never told. This server sends the player's id.
  The replay stops before that step, so the flow tests check it, and a planted
  expulsion carrying the character's id fails them.
- **A random pick the client cannot play.** The client's map list offers four
  random picks beside its maps, and sends the one the host picks as it is.
  Nothing in the client turns a random pick into a map, and a real client told to
  start a match on one shows a black screen. So a room keeps the pick as picked,
  as every client shows it, and the match is left to decide the map. The first
  plan for this stage had said rooms of teams had no random map at all; the
  client's own map list showed otherwise.
- **A turn that would have left a client without enemies.** Every client plays a
  mission level's opening, which places the level's enemies, and reports when it is
  over. The first plan, like the recorded sessions, started the level's first
  turn on the report of the player whose turn it was. The
  client's own code showed that a turn's start ends whatever its mission was doing,
  the opening included, so a client slower than that player would never place the
  level's enemies. The first turn now waits for every client, for ten seconds at
  most, and a planted version that starts it on the first report fails the tests.
- **A level asked for by everyone.** Every client asks for each level, so in a room
  of two every level is asked for twice, and each announcement restarts the
  level's opening on every client. Only the next level begins now, once; the copies
  change nothing, and a planted version that begins any later level fails the
  tests.
- **A mission cleared at once.** A client asks for the level after the last when it
  has none: the mission is cleared. Taken at any moment, that request would let a
  modified client clear a mission at once. The server cannot count kills, which
  happen in each client's play of a shot, but it accepts the clear only while the
  last level runs.
- **A wait that started after the news.** The first version told the room a level
  had begun and only then started the wait for its reports. A client, or a test on
  a clock it moves itself, could act on the news before the wait existed, and the
  wait would then never end for it. Writing the test on that clock showed it: the
  wait now starts before anyone is told.
- **A layout declared otherwise than it is read.** The reference data declares the
  report a client sends once it has loaded as a list of objects, each led by its
  own id. The client writes each entry as a slot and a position, with no id, which
  the recorded bytes confirm. The check reads it as the client does, by a layout
  given once from the client's own writer.
- **A turn that started on top of a shot.** The recorded sessions end a turn on
  the shooter's report alone. The client's own code showed that a turn's start
  does not wait for a shot the client is still playing, and that a death in a shot
  reaches the server only from the dying character's own client, as it plays the
  shot back. A turn started on the shooter's report could start on top of a slower
  client's shot, and go to a character that client was about to see die. The turn
  now waits for every client's report, and a planted version that passes it on the
  first report fails the tests.
- **Every shot reported twice.** A real client sent two reports for one shot. Its
  code shows why: every shot ends with a mark a second after its last step, and the
  client reports when it reaches the mark and again once the last step is behind
  it, and every client that plays the shot does so. Counted as they come, the
  shooter's two reports could stand for two clients and end the turn before the
  others had finished, or end it twice. Each client now counts once per shot, and
  its second report is not even logged.
- **A copy back that would come round again.** A client sends on to the server any
  shot, pass or fall it is given that names one of its own characters. The recorded
  sessions send a fall back to its sender too, which would loop. A shot, a pass and
  a fall go to every other client and never back.
- **A level begun in the middle of a shot.** Levels began on the first client's
  request, which was right while no shots were played. With shots, that request
  comes from the first client to finish one, while the others are still in it, and
  a level's opening would start on top of their shot. A later level is now held
  until every client has played the shot back, and so is the mission's clear.
- **A test that passed because a wait ran out.** A planted bug made a player's
  leave no longer end the shot it was being waited for, and the test of that leave
  still passed: on the real clock the shot's own wait, five seconds, passed the turn
  anyway, within the ten seconds a test waits for a packet. Every test of turns now
  runs on a clock that moves only when the test moves it, so a turn passes only for
  the reason the test gives it, and the planted bug fails the test at once.
- **A test that logged in again before the server had let go.** A test of the
  tutorial closed its connection and logged in again with the same account at
  once. The server frees an account only once it has finished with the old
  connection, and refuses a login that comes sooner, since an account is live on
  one connection at a time. Rooms made that cleanup longer, as a closing
  connection now leaves its room, lobby and channel first, so a test that had
  passed since it was written began to fail now and then, and failed the build of
  `main` twice. With the cleanup slowed on purpose by 50 milliseconds it failed
  every time. It now waits for the server to finish with the first connection, as
  the other tests that log in again already did, and with the same slowdown every
  test passes.
- **Items that must come back to their sender.** A shot, a pass and a fall go to
  every other client and never back. The recorded sessions send an item used back
  to its sender as well, and the client's own code shows why: it sends an item
  used, spent, thrown away, caught or given as a bonus, and acts on it only when the
  server's copy comes back. Passed only to the others, an item would never reach
  its own user's screen. Each now goes to everyone, its sender included, and where
  an item went goes back to its sender alone; a planted version that leaves the
  sender out fails the tests.
- **A turn count that never moved.** A real client showed a mission level's turn count
  stuck at the first turn: the start of every turn carries the count the client shows,
  and every one carried 0. It now carries how many shots the level's players have
  fired, which every level starts again from none. A planted version that counts every
  turn, passes included, and one that never starts again, both fail the tests.
- **A ghost moved after its player left.** The start of every turn moves each ghost
  on its way until its client reports it there. A player who leaves takes its ghost
  off every field, but the ghost's target stayed behind, so every turn after would
  have moved a ghost no client had. The client skips a ghost it does not have, so
  nothing broke; the server now forgets a ghost with its player, and a planted
  version that keeps it fails the tests.
- **Every ghost on one team.** A mission's players all sit on the first team, so
  every ghost was sent as the first team's. In a match between teams, the second
  team's dead would have come back as the first team's. Each ghost now carries the
  team of its slot, and a planted version that sends the first team's fails the
  tests.
- **A wait counted from the wrong moment.** A client reports a match between teams
  loaded only once its camera has toured every character, two seconds each, so in a
  full room no client can report before sixteen seconds have passed. The first plan
  gave the clients five seconds from the start, which would have cut every client's
  tour short in a full room, and a turn reaching a client still touring sets its tour
  going again over the turn. The five seconds now run from the first client's report,
  every client touring the same characters at the same pace, with `Matches:LongestLoad`
  from the start in all, and the server refuses to start with a `LongestLoad` no
  longer than a full room's tour.
- **A report naming another player's slot.** One recorded client reports its match
  loaded for a slot that is not its own. Dropped whole, that report would have left
  the first turn waiting for its client until the wait ran out. A report now counts
  for its client whatever slots it names, since only the client knows it has loaded,
  and only the places it gives are dropped; a planted version that drops the whole
  report fails the tests.
- **A test that asked at the wrong moment.** A match between teams drops a
  mission's requests, and the first test sent them during a turn, where a rule
  older than these matches drops a level request anyway. A planted version without
  the new check passed it. While the clients load, nothing else stops a level
  request, and it would have begun a mission's level in the middle of a match
  between teams; the test now asks there too, and the planted version fails it.
- **A list the recordings only ever had empty.** A shot carries two lists, and
  the codec read both as lists of steps, which every recorded shot agreed with:
  the second list was empty in each of them. With a real client, the weapons whose
  missiles leave an effect on the characters they hurt sent a shot the codec
  refused, and the shooter was disconnected. The client's own code fills the
  second list with those effects and nothing else, and every client gives them to
  the characters the shot hurts. The codec now reads that list as effects only,
  their two values as the floats the client keeps them as. Since the server passes
  on what it decodes, written back again, the tests send effects whose values are
  a NaN with a payload and a negative zero, and check the other client gets the
  shot byte for byte; planted versions that read or write a value as a number
  rather than as its bits, swap two fields, drop an effect, or take a step for an
  effect fail the tests. Where every recording leaves a field empty, a passing
  replay says nothing about it.
- **A NaN the compiler quieted.** Those tests first built the NaN as a float in
  code, from its bits. They passed in the Debug builds run before committing, but
  in a Release build, as the CI runs them, the compiler worked that float out ahead
  and set the bit that quiets a NaN, before any codec ran. The codec's test failed,
  and the match test passed only because it then compared a quiet NaN with itself.
  Both now put the two values on the wire as bits, as a client does, and versions
  planted to change a value's bits as they are read or written fail both, in
  either build.
- **A mode taken at its name.** A room's game mode is either with items or
  without, and the server took "without items" to mean no item boxes: the start of
  every turn in `NoItems` (mode 1) let none drop, and a test pinned that down. The
  tests passed because they checked what the server had been told to do. Nothing
  checked what the client does with a box in that mode, and the check with a real
  client was planned but never done. The client's own code drops boxes in every mode, as
  many as the start of the turn allows, and in the modes without items it turns a
  box caught, or a bonus, into its team's gauge instead of an item, which is what
  that mode is about. The server now lets boxes drop in every mode. The tests check
  that the first turn and the next allow as many boxes without items as with them,
  and that a catch and a bonus carrying a gauge change and no item reach every
  client unchanged; versions planted to let none drop without items, on the first
  turn or a later one, to pass a catch or a bonus on without its gauge change, or to
  drop a catch with no item, all fail them. A rule taken from a name is a guess
  until the client's code or a real client confirms it.
- **A captain named in every match.** The start of every match carried the
  captains the reference sessions carry, slot 1 for the first team and slot 0 for
  the second, and the tests checked those values, so they passed. Nothing checked
  what the client does with them. The client takes a character whose slot is its
  team's entry as that team's captain, in any game mode, and fires every shot a
  captain fires twice. So whoever sat in slot 1 fired twice in every match between
  teams and every mission, and the second team never had a captain, since slot 0
  is on the first. A player who saw a captain fire twice in a turn asked why, and
  the client's code answered it. The start now names no captain, with a number no
  slot has. The tests check that a mission with a character in slot 1, and a match
  between two teams of two in either mode without captains, name none; versions
  planted to name the old slots, the first team's alone, slot 0 for the unused
  entry, or a real slot as "no captain", all fail them, and so does a replay that
  keeps the recorded captains. A value copied from a recording is a choice the
  recording made, and needs the client's code to say what it does.
- **One password, two forms.** Keeping only a hash of a password works only if
  the client sends the same text when it creates an account and at every login,
  so before the server stopped keeping that text, the client's code was read for
  both. Both build it from the same password box in the same way, 32 lower-case
  hex digits of an MD5; only the writing differs, wide in the request to create
  and one byte per character in the login. The test of it builds both requests
  byte by byte, as the client writes them, rather than with this server's own
  writers, which could share a mistake with its readers. The hash function is
  checked against the values RFC 7914 publishes, since a hash checked only
  against itself passes with any function; a version planted with SHA-1 fails
  that test and no other.
- **Two rules for one name.** Names are unique whatever their case. The database
  keeps them unique on a key the server works out, the name in capitals, since
  its own case rules change only ASCII letters; the in-memory store compared names
  with .NET's case-insensitive comparison. A test that tries every letter a name
  may hold against every other found that the two disagree on one, the long s, ſ,
  which the comparison takes as another letter than s and the capitals write as S.
  So the in-memory store would have let a character be named Kiſi beside a Kisi
  that the database refuses, and the flow tests, which run on that store, would
  have spoken for a rule the deployment does not keep. The comparison is now built
  from the key, and both stores pass a test with those two names.
- **A requirement checked only against itself.** The tests of the startup check
  were written against its own number, so a server that asked for an older
  migration than its stores need would have passed them all and failed at its
  first query. A test now builds a database migrated only up to that number, with
  Flyway's own target, and runs every store on it.
- **An expectation the code under test wrote.** Before every turn between teams,
  the server places item boxes in a shot of its own, and the tests read that shot.
  At first they checked it against the server's own builder of it, given the boxes
  they had read, so a bug in that builder, such as steps naming no character, would
  have been in the expected shot as well as the sent one, and the tests would have
  passed. They now check what the client needs, written out: the shot sent for
  slot 0xFE, which no character has, and every step of the type the client plays as
  a box, naming the character whose turn starts, at the shot's start. Versions
  planted to send the shot for the turn's slot, to have its steps name no character,
  or to give them another type all fail them. An expectation comes from what the
  client does, never from the code it checks.
- **One answer per connection, not per character.** A connection may play two
  characters, and a mission's enemies' turn reads at first as something each
  character takes part in, to be sent to each and answered by each. The client's
  code says otherwise: it runs one mission for the whole game, moves its enemies
  once for every enemies' turn it is sent, and answers each once, however many
  characters it plays. So the server sends each enemies' turn once to every
  connection and waits for one answer from each. A test plays a connection with
  two characters: a second enemies' turn sent to it would arrive where the test
  expects the next turn, and a wait for two answers would never end. Versions
  planted to send it once per character, or to wait for one answer per character,
  fail it. The same reading showed that the enemies never attack: their turn only
  moves them, places the level's next wave and lets their own poison work, which
  the roadmap had called "missions whose enemies fight back".
- **A race the key did not cover.** What a match pays is kept once by a payment
  row keyed by the match and the character. The first race kept one match 64 times
  at once, and every keeping after the first found the row there and paid no one:
  it passed with the character's row no longer locked while it is paid. Two
  matches paying one character at once each insert a row of their own, so the key
  does not keep them apart: each read the same balance, added its own pay, and the
  second write lost the first's. A second race, 64 matches paying one character at
  once, fails without the lock. The two races also opened 64 connections each,
  and one run, with a race in another test class alongside, asked PostgreSQL for
  more than its 100: the test classes that use the database now run one after
  another.
- **Rules no test could tell from their mistakes.** Of 25 versions planted in the
  payment code, three passed every test. A rule that a player who leaves counts the
  match lost was never seen to do anything, since every test's leaver was on the
  losing team. Rounding instead of dropping a fraction matched every shipped amount,
  since .NET rounds a half to the even number and every half there fell on one. And
  the test that runs every store on a database at exactly the migration the server
  asks for never kept a match, so asking for the migration before the payments' own
  passed. A leaver on the winning team, an amount whose half rounds up, and a
  match kept on that database now fail them.
- **A card's position is not its reward.** The client's result screen finds a
  reward by the character id of the player whose card was turned, among eight
  non-null offers it received before the match ended. Changing which card that
  player picks must therefore leave the offer's kind and amount alone. The
  server keeps the offer with the match, and keeps a successful pick and its
  payment in one transaction. Flow tests turn a card at a position unlike the
  character's slot and check its own offer, while store tests retry a pick whose
  commit answer was lost and check that it was credited once. A refused pick
  buys nothing.
- **A death report needs a shot.** A client asks to become a ghost when its own
  character plays a death step. A fall uses its own report. The result tests
  play both kinds and send duplicate and late reports. Kill credit requires a
  death step for that victim in the active shot, and only a credited kill pays
  its teammates assist points. The result tests also look at the card offer
  before it is picked. That offer must use the same base pay as the scoreboard.

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
supplied locally. The conformance tests read them from there, and skip when they
are absent rather than passing quietly: CI reports them as skipped, with the
reason, so an empty run cannot be mistaken for a clean one.

Each codec is checked against them three ways, and only one of the three goes
through the codec in both directions:

- **Replay.** Every recorded body of an implemented packet, from either side, is
  read and written back, and must come out byte for byte the same.
- **The declared layout.** Where the reference data declares a packet's fields,
  the recorded bytes are read from that declaration alone and compared, field by
  field, with what the codec read. A codec that reads and writes the same field
  in the same wrong place passes the replay and fails here. Fields are paired
  with the codec's record by position, not by name, so a field can be named for
  what it holds; objects are read by their own class's declaration, whether they
  sit in a list or in a field of their own, so nested layouts are checked too. A
  few fields are declared only as an object written inline, with no layout of its
  own, such as a ghost's position, or only as bytes the reference data computes,
  such as the report the client makes on its own code at the end of a room
  request; their parts are given once in the check, from the client's own writer,
  and paired with the codec's next fields. Some classes have no usable
  declaration at all, because which fields they carry depends on their own
  values: a room record ends at a status of 0, a room's slot carries a character
  only when it is taken, and an entry of the lobby's room list carries the fields
  its mask names, in an order of its own. Those are read by layouts given once in
  the check, from the client's own reader and apart from the codecs, and each part
  is compared with the codec's field of the same name. A few packets have no
  declaration at all, such as the start of a match or of a turn; they are read by
  layouts from the client's own reader too, and the objects they hold, which are
  declared, are read by their own declarations, so a field swapped inside a
  character's starting values is caught there. A result card has a declaration,
  but its record derives its kind from its class instead of holding it, so it is
  read by the client's layout and compared by name. The report a shot ends with is
  declared only as an object, written inline and kept as one record, so its parts,
  from the client's own writer, are paired with that record's. Packets no recording
  carries, and shapes no recording shows, such as a room list with rooms in it, a
  turn that moves a ghost, or a shot with a step of every kind, where the recorded
  shots hold none or only item boxes, are written from invented values and checked
  the same way. So are the item packets: every recorded one is for slot 0 with most
  of its values 0, where a swap of two fields would read the same. A declared type the check does not know fails it rather than being
  skipped.
- **The registry.** Each packet names itself once, in a `[WireClass]` attribute,
  and its id is computed from that name the way the client computes it. The
  computation is checked against every id in the reference table, not only the
  implemented ones, so a mistake in it cannot hide behind a round trip.

The server itself is checked a fourth way. Every recorded session is played
against real sessions over loopback sockets, each participant on a connection of
its own and greeted with the handshake values the session recorded, and every
reply, the greeting included, must match the recording byte for byte. What a
step sends is compared on the connection it goes to, so when a guest enters a
room, the slot its host is told of is checked on the host's connection. A
participant is followed until it sends something that belongs to a later stage,
and one that does so while in a room ends the whole session's replay there: what
everyone else does next can depend on what it would have done in the room. So the
recorded rooms are played through a guest getting ready, the host's options, a
slot closed, a player entering, a team change, a weapon and a quick join. A
mission is played through its start, with every character's starting values and
the mission's seed, and through every client loading it. A match between teams is
played through its start, every client loading it, its first turn, and that turn's
walking and items, up to a box a ghost catches. Every later recorded turn belongs
to such a match, so turns are checked through real connections instead.
A recorded answer can also hold packets of later stages; a recorded packet the
server has no codec for is not expected, and every other one is, in order. One
answer differs by design: the recordings move to another channel by reconnecting,
and this server keeps the player on its connection, so that move is checked as
entered instead, and the play time the recording grants on the connection it
reconnects with is expected ahead of that answer, with no limit. And the lobby's
player list, and the changes to its room list, are not compared at all: the
recordings list every lobby's players as empty and never add or remove anyone or
change a room, where this server lists who is there and tells them of each player
who comes or goes and each room that opens, changes or closes. These are checked
through real connections instead, player by player and change by change. A few
recorded values are placeholders, such as a room's team names, or the closed slots a recorded mission start lists
for the mission's own side; each is set to what this server sends before
comparing, and the replay names every one of them. Three kinds of recorded step
end their session by design: a recorded mission reports a level ready before it
asks for one, where a real client asks for the first level once it has loaded; a
box a ghost catches is passed on to every client, where the recording answers
none; and a player giving up is passed on to no one, since no client does anything
with it, and its match's end pays nothing yet, where the recording passes it on and
pays. A match's seed, the cards picked for players and the item boxes each turn
allows are as the recordings have them.

Codecs are written by hand and published one stage of the protocol at a time,
so the repository shows the layouts this server implements and nothing more.

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
