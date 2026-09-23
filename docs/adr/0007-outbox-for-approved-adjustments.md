# 7. Approved adjustments go through a transactional outbox

- Status: Accepted
- Date: 2026-09-22

## Context

A character's gold lives in the game server's state, and that state does not
exist yet. Even when it does, the admin server cannot change it by writing to
the database behind the game server's back: a running game server holds the
live value and would overwrite the change. An approval therefore has to reach
the game server as a message. The approval and the message must never
disagree: an approved adjustment must not be lost, and a rolled-back one must
never be sent.

## Decision

An approval writes an `economy.adjustment.approved` event to `outbox_events` in
the same database transaction as the approval itself. A separate dispatcher
will read unpublished events, deliver them to the game server over the control
channel, and mark them published. `Outbox.record` requires a surrounding
transaction and fails without one.

## Alternatives considered

- **Calling the game server during the approval.** If the call succeeds and
  the commit fails, or the other way round, the two sides disagree. Making
  both succeed or fail together would need a distributed transaction, which
  neither side supports.
- **Writing gold directly into the database.** The running game server owns
  the live value, as explained above.
- **A message broker.** Publishing to a broker from inside the transaction has
  the same two-system problem as a direct call. The outbox is the standard way
  to feed a broker safely, so one can still be added behind the dispatcher
  later.

## Consequences

- Approvals are safe to make today, before the game server can apply them.
- Delivery will be at least once: a dispatcher that crashes after delivering
  but before marking the event published delivers it again. The game server
  must apply each adjustment once, keyed by its id.
- Unpublished events need watching. An event that stays unpublished is a stuck
  adjustment and should raise an alert once the dispatcher exists.
