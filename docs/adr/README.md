# Decision records

Each file records one decision that shapes the project: the situation that
called for it, what was decided, what else was considered, and what follows
from it. They are short on purpose. A new decision gets the next number; a
decision that is reversed keeps its file, marked as superseded by the one that
replaces it.

| # | Decision | Status |
|---|---|---|
| [0001](0001-one-repository.md) | One repository for both servers, the contracts, the database and the deployment | Accepted |
| [0002](0002-two-runtimes.md) | .NET for the game server, Spring Boot for the back office | Accepted |
| [0003](0003-contracts-before-code.md) | Contracts are written before the code, in language-neutral files | Accepted |
| [0004](0004-migrations-own-the-schema.md) | The migrations own the schema; the services only validate it | Accepted |
| [0005](0005-roles-in-keycloak-permissions-in-code.md) | Operators sign in through Keycloak; roles live there, permissions in code | Accepted |
| [0006](0006-dual-control-and-idempotency-enforced-twice.md) | Dual control and idempotency are enforced in the code and again in the database | Accepted |
| [0007](0007-outbox-for-approved-adjustments.md) | Approved adjustments go through a transactional outbox | Accepted |
| [0008](0008-audit-chain-built-by-the-database.md) | The database builds the audit trail's hash chain; the admin server checks it | Accepted |
| [0009](0009-secrets-are-files.md) | Secrets are files, and no credentials are committed | Accepted |
| [0010](0010-no-game-client-files.md) | No game client files in the repository | Accepted |

## Format

```markdown
# <number>. <the decision, as a sentence>

- Status: Proposed | Accepted | Superseded by <number>
- Date: YYYY-MM-DD

## Context
## Decision
## Alternatives considered
## Consequences
```
