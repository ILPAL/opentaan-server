-- Economy adjustments under dual control, and the outbox that carries approved
-- adjustments to the game server.
--
-- The rules the admin server enforces are also constraints here, so a bug in the
-- application cannot break them: a non-zero amount, a reason, a decision that
-- matches the status, and a decider who is not the requester.

CREATE TABLE economy_adjustments (
    adjustment_id      UUID        PRIMARY KEY,
    -- No foreign key: players can delete characters, and a record of money moved
    -- must outlive the character it was moved for.
    character_id       BIGINT      NOT NULL,
    delta_gold         BIGINT      NOT NULL CHECK (delta_gold <> 0),
    reason             TEXT        NOT NULL CHECK (length(btrim(reason)) > 0),
    status             TEXT        NOT NULL
                                   CHECK (status IN ('PENDING_APPROVAL', 'APPROVED', 'REJECTED')),
    approval_required  BOOLEAN     NOT NULL,

    requested_by_id    TEXT        NOT NULL,
    requested_by_name  TEXT        NOT NULL,
    requested_at       TIMESTAMPTZ NOT NULL,

    decided_by_id      TEXT,
    decided_by_name    TEXT,
    decided_at         TIMESTAMPTZ,
    decision_note      TEXT,

    -- The client's key for this request, and a hash of the request itself, so a
    -- retry is recognised and a reused key with a different body is refused.
    idempotency_key    TEXT        NOT NULL,
    request_hash       TEXT        NOT NULL,

    -- Optimistic locking: of two concurrent decisions, exactly one succeeds.
    version            BIGINT      NOT NULL DEFAULT 0,

    CONSTRAINT one_key_per_request UNIQUE (requested_by_id, idempotency_key),

    -- Four-eyes: whoever decides is never whoever asked.
    CONSTRAINT decider_is_not_requester
        CHECK (decided_by_id IS NULL OR decided_by_id <> requested_by_id),

    -- A pending adjustment has no decision; a decided one has a time.
    CONSTRAINT decision_matches_status CHECK (
        (status = 'PENDING_APPROVAL' AND decided_at IS NULL AND decided_by_id IS NULL)
        OR (status <> 'PENDING_APPROVAL' AND decided_at IS NOT NULL)
    ),

    -- Only an adjustment that needed approval can have been decided by a person;
    -- one below the threshold is approved on request, by no one.
    CONSTRAINT decider_only_when_required CHECK (
        approval_required OR (status = 'APPROVED' AND decided_by_id IS NULL)
    ),

    -- A rejection always says why.
    CONSTRAINT rejection_has_note CHECK (
        status <> 'REJECTED' OR length(btrim(coalesce(decision_note, ''))) > 0
    )
);

CREATE INDEX economy_adjustments_by_status ON economy_adjustments (status, requested_at DESC);
CREATE INDEX economy_adjustments_by_character ON economy_adjustments (character_id, requested_at DESC);

-- Transactional outbox. An event is written in the same transaction as the change
-- it describes, so an approved adjustment is never lost between the database and
-- the game server; a separate dispatcher delivers events and marks them published.
CREATE TABLE outbox_events (
    event_id        UUID        PRIMARY KEY,
    aggregate_type  TEXT        NOT NULL,
    aggregate_id    TEXT        NOT NULL,
    event_type      TEXT        NOT NULL,
    payload         TEXT        NOT NULL,
    created_at      TIMESTAMPTZ NOT NULL,
    published_at    TIMESTAMPTZ
);

CREATE INDEX outbox_events_unpublished ON outbox_events (created_at) WHERE published_at IS NULL;
