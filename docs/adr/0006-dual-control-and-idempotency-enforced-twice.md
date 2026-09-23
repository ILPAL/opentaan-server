# 6. Dual control and idempotency are enforced in the code and again in the database

- Status: Accepted
- Date: 2026-09-22

## Context

A gold adjustment changes what a player owns. Two failures matter most: one
person moving a large amount alone, and one intended adjustment being applied
twice because a request was retried or double-clicked. Both have to hold even
when two requests arrive at the same moment, and even if a later code path,
in this service or another, forgets the rule.

## Decision

- **Dual control.** An adjustment at or above a configured threshold waits for
  a second operator, and the requester can never decide it. The
  `EconomyAdjustment` entity enforces this, next to the data it protects. The
  database enforces it again with constraints: `decider_is_not_requester`,
  `decision_matches_status`, `decider_only_when_required` and
  `rejection_has_note`.
- **Idempotency.** A request carries an `Idempotency-Key` header. The key is
  unique per operator in the database, and the request's content is hashed
  and stored with it:
  - the same key with the same content replays the original result, marked
    `Idempotent-Replayed: true`;
  - the same key with different content is refused with 409;
  - of two identical requests racing each other, the unique constraint decides
    the winner, and the loser answers with the winner's result.
- **Concurrent decisions.** The adjustment row carries a version, so of two
  operators deciding at once, exactly one succeeds and the other gets 409.

## Alternatives considered

- **Rules in the code only.** A future code path, a manual SQL fix or the game
  server could bypass them.
- **Rules in the database only.** Callers would then learn about a broken rule
  from a constraint name instead of a clear error, and the rules would be
  invisible to anyone reading the Java.
- **Checking for an existing key before inserting.** Two concurrent requests
  would both see no key and both insert. Only a unique constraint decides the
  race reliably.

## Consequences

- Each rule exists twice, so the two must be kept in agreement. Unit tests
  cover the entity, and integration tests on PostgreSQL show that the database
  refuses what the code would.
- Idempotency keys are scoped to the operator, so two operators cannot collide
  or replay each other's requests.
- The threshold is configuration (`opentaan.economy.approval-threshold-gold`), so
  changing it is a reviewed change, not a runtime setting.
- Two operators colluding can still approve each other's adjustments. Dual
  control makes that take two people, and the audit trail records both (see
  [0008](0008-audit-chain-built-by-the-database.md)).
