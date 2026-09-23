# 8. The database builds the audit trail's hash chain; the admin server checks it

- Status: Accepted
- Date: 2026-09-22

## Context

Every operator action has to be recorded: each change, and each read of a
player's personal data. The record must be complete, so that no action is
missing and none is recorded that did not happen. It must also show whether
anyone has changed or removed an entry afterwards. The admin server writes to
the trail today, and the game server will write to it later. Two services in
two languages must not be trusted to implement the chaining the same way.

## Decision

- **Same transaction.** `AuditTrail.record` appends an entry inside the
  action's own transaction and fails if there is none. A rolled-back action
  leaves no entry, and a retry that changes nothing records nothing.
- **Append-only in the database.** A statement-level trigger refuses `UPDATE`,
  `DELETE` and `TRUNCATE` on `audit_entries`, for every role.
- **The chain is built by the database.** A `BEFORE INSERT` trigger takes a
  transaction-scoped advisory lock and gives each entry the next sequence
  number. It stores the previous entry's hash in the new entry and computes
  the new entry's own SHA-256 over its fields and that link. Fields are
  encoded as netstrings, so text cannot move between fields and keep the same
  hash. Whatever the caller supplies for these columns is replaced. A unique
  constraint on the previous hash makes a fork impossible, whatever the
  isolation level.
- **The chain is checked by the application.** The admin server implements the
  hash definition again in Java. `GET /api/audit/verification` recomputes the
  whole chain and reports the first entry that does not fit. A known-answer
  test, with a hash taken from PostgreSQL, keeps the two implementations in
  step.

## Alternatives considered

- **The application computes the hashes.** Each writer would need its own
  implementation and the same locking, and a writer with a bug, or one that
  skipped the step, would break the chain for everyone.
- **HMAC with a key the database does not hold.** Someone with only database
  access could not rebuild the chain. The price is key management and
  rotation, a key in every writing service, and entries that no auditor can
  check without that key. This is worth revisiting if the threat model gains
  an attacker who controls the database but not the services.
- **An external append-only store** (a ledger database, WORM object storage, a
  SIEM). This gives stronger guarantees, but the entry would no longer commit
  in the same transaction as the action. It is a better place for periodic
  copies of the chain head than for the trail itself.
- **Application logs.** They are not transactional, not queryable by auditors
  and easy to lose.

## Consequences

- Audited transactions append one at a time, from the audit insert to the
  commit. The audit write is therefore the last write of each transaction, and
  a decision is flushed before its entry so that a concurrent decision fails
  before the lock is taken. At back-office volumes the cost is negligible. A
  load run of sixteen concurrent writers inserting 1,600 entries, and an
  integration test with eight writers, each produced one gap-free, correctly
  linked chain.
- The rules live in PL/pgSQL, a second language to review. Integration tests
  cover each rule against a real PostgreSQL, including tampering with the
  trigger switched off.
- **Limit:** whoever owns the table can switch the trigger off, change an entry
  and rebuild every hash after it. The chain then verifies again. Detecting
  this needs a copy of a recent chain head kept elsewhere: an auditor records
  `lastHash` from a verification and later checks that the same entry still
  has it. Making that copy automatic, and running the services as a database
  role that cannot switch triggers off, are open items in the
  [threat model](../security/threat-model.md).
