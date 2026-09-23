-- The audit trail: one entry for every operator action, in the order the actions
-- were committed, which nothing can change or remove afterwards.
--
-- Three things protect it, all here in the schema so that neither service can
-- get them wrong:
--
--   1. The table is append-only. A trigger refuses UPDATE, DELETE and TRUNCATE,
--      whoever asks.
--   2. The entries form a hash chain. Each entry stores the SHA-256 hash of the
--      entry before it, and its own hash covers that. Changing or removing an
--      entry breaks the chain at that point, and a verifier can say where.
--   3. The database numbers and hashes each entry as it is inserted, one writer
--      at a time. A service supplies what happened; it cannot choose where the
--      entry goes in the chain or what its hashes are.
--
-- The hash of an entry is SHA-256 over its fields in this order: sequence,
-- entry_id, occurred_at, actor_id, actor_name, action, subject_type, subject_id,
-- details, previous_hash. Each field is written as a netstring,
-- "<length in UTF-8 bytes>:<value>,", so no text inside a field can make two
-- different entries hash the same. occurred_at is written in UTC as
-- YYYY-MM-DDTHH:MM:SS.ffffffZ. The first entry's previous_hash is 64 zeros.
-- The admin server re-implements this to verify the chain independently.

CREATE TABLE audit_entries (
    -- Position in the chain: 1, 2, 3, ... without gaps. Assigned by the trigger.
    sequence       BIGINT      PRIMARY KEY CHECK (sequence > 0),
    entry_id       UUID        NOT NULL UNIQUE,
    occurred_at    TIMESTAMPTZ NOT NULL,

    -- Who acted. The name is kept as it was at the time, for reading later.
    actor_id       TEXT        NOT NULL CHECK (length(actor_id) > 0),
    actor_name     TEXT        NOT NULL,

    -- What they did, such as economy.adjustment.approved, and to what. No foreign
    -- key: the record of an action must outlive the thing it was done to.
    action         TEXT        NOT NULL CHECK (action ~ '^[a-z]+(\.[a-z]+(-[a-z]+)*)+$'),
    subject_type   TEXT        NOT NULL CHECK (subject_type ~ '^[a-z]+(-[a-z]+)*$'),
    subject_id     TEXT        NOT NULL CHECK (length(subject_id) > 0),

    -- The facts of the action, as a JSON object. Stored as the exact text the
    -- service wrote, because the hash covers those bytes.
    details        TEXT        NOT NULL CHECK (details IS JSON OBJECT),

    previous_hash  TEXT        NOT NULL CHECK (previous_hash ~ '^[0-9a-f]{64}$'),
    entry_hash     TEXT        NOT NULL CHECK (entry_hash ~ '^[0-9a-f]{64}$'),

    -- Every entry has at most one successor, so the chain cannot fork.
    CONSTRAINT one_successor_per_entry UNIQUE (previous_hash)
);

CREATE INDEX audit_entries_by_subject ON audit_entries (subject_type, subject_id, sequence DESC);
CREATE INDEX audit_entries_by_actor ON audit_entries (actor_id, sequence DESC);

-- One field of the canonical form.
CREATE FUNCTION audit_netstring(value TEXT) RETURNS TEXT
    LANGUAGE sql STABLE STRICT
    RETURN octet_length(convert_to(value, 'UTF8')) || ':' || value || ',';

CREATE FUNCTION audit_entry_hash(entry audit_entries) RETURNS TEXT
    LANGUAGE sql STABLE STRICT
    RETURN encode(sha256(convert_to(
        audit_netstring(entry.sequence::text)
        || audit_netstring(entry.entry_id::text)
        || audit_netstring(to_char(entry.occurred_at AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'))
        || audit_netstring(entry.actor_id)
        || audit_netstring(entry.actor_name)
        || audit_netstring(entry.action)
        || audit_netstring(entry.subject_type)
        || audit_netstring(entry.subject_id)
        || audit_netstring(entry.details)
        || audit_netstring(entry.previous_hash),
        'UTF8')), 'hex');

-- Links a new entry to the end of the chain. Whatever the caller put in
-- sequence, previous_hash and entry_hash is replaced.
CREATE FUNCTION audit_entries_link() RETURNS trigger
    LANGUAGE plpgsql
AS $$
DECLARE
    last_sequence BIGINT;
    last_hash     TEXT;
BEGIN
    -- One writer at a time, keyed by this table's oid. The lock is held until the
    -- writing transaction ends, so the next writer reads this entry as the end of
    -- the chain. (Under REPEATABLE READ or SERIALIZABLE the next writer's snapshot
    -- may predate this entry; it then fails on the primary key rather than fork
    -- the chain.)
    PERFORM pg_advisory_xact_lock(TG_RELID::bigint);

    SELECT sequence, entry_hash INTO last_sequence, last_hash
    FROM audit_entries
    ORDER BY sequence DESC
    LIMIT 1;

    NEW.sequence := coalesce(last_sequence, 0) + 1;
    NEW.previous_hash := coalesce(last_hash, repeat('0', 64));
    NEW.entry_hash := audit_entry_hash(NEW);
    RETURN NEW;
END;
$$;

CREATE TRIGGER audit_entries_link
    BEFORE INSERT ON audit_entries
    FOR EACH ROW EXECUTE FUNCTION audit_entries_link();

-- Raised as an integrity violation (SQLSTATE 23001), so clients report it as one.
CREATE FUNCTION audit_entries_refuse_change() RETURNS trigger
    LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'audit_entries is append-only: % is not allowed', TG_OP
        USING ERRCODE = 'restrict_violation',
              HINT = 'Record a new entry that corrects the earlier one.';
END;
$$;

-- Statement-level, so the refusal does not depend on any row matching.
CREATE TRIGGER audit_entries_append_only
    BEFORE UPDATE OR DELETE OR TRUNCATE ON audit_entries
    FOR EACH STATEMENT EXECUTE FUNCTION audit_entries_refuse_change();
