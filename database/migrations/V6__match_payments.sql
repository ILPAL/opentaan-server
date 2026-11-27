-- The matches the game server has ended, and what each paid the characters that started
-- it, each payment made once.
--
-- V1 made tables for match history and for idempotency claims in another shape, which
-- nothing ever wrote or read. They are replaced here.
--
-- A payment is a row keyed by its match and its character, written in the same
-- transaction as the change to the character it pays. Keeping a match again finds the
-- row there and pays no one twice. A payment names its character by id, with no foreign
-- key, so that what a match paid stays on record when the character is deleted. A
-- character's team is not stored: it is the team of its slot.

DROP TABLE match_participants;
DROP TABLE matches;
DROP TABLE idempotency_claims;

CREATE TABLE matches (
    match_id           UUID        PRIMARY KEY,
    room_number        INTEGER     NOT NULL CHECK (room_number BETWEEN 0 AND 65535),
    channel_id         INTEGER     NOT NULL CHECK (channel_id BETWEEN 0 AND 65535),
    -- The map played on, or in a mission, the mission's own number.
    map_id             INTEGER     NOT NULL CHECK (map_id BETWEEN 0 AND 65535),
    -- The difficulty a mission was played at; NULL for a match between teams.
    mission_difficulty SMALLINT    CHECK (mission_difficulty BETWEEN 0 AND 2),
    winning_team       SMALLINT    NOT NULL CHECK (winning_team IN (1, 2)),
    started_at         TIMESTAMPTZ NOT NULL,
    ended_at           TIMESTAMPTZ NOT NULL,
    CONSTRAINT ends_after_it_starts CHECK (ended_at >= started_at)
);

CREATE INDEX matches_by_end ON matches (ended_at);

CREATE TABLE match_payments (
    match_id     UUID     NOT NULL REFERENCES matches (match_id),
    character_id BIGINT   NOT NULL CHECK (character_id BETWEEN 1 AND 4294967295),
    slot         SMALLINT NOT NULL CHECK (slot BETWEEN 0 AND 7),
    -- How the character's part ended: seen to the end, given up, or left before it.
    ending       TEXT     NOT NULL CHECK (ending IN ('played', 'gave_up', 'left')),
    won          BOOLEAN  NOT NULL,
    experience   BIGINT   NOT NULL CHECK (experience BETWEEN 0 AND 4294967295),
    gold         BIGINT   NOT NULL CHECK (gold BETWEEN 0 AND 4294967295),
    PRIMARY KEY (match_id, character_id),
    CONSTRAINT one_character_per_slot UNIQUE (match_id, slot),
    -- A character that gave up or left is paid nothing.
    CONSTRAINT paid_only_for_playing CHECK (ending = 'played' OR (experience = 0 AND gold = 0)),
    -- A character whose player left loses: its client counts nothing.
    CONSTRAINT leaving_loses CHECK (ending <> 'left' OR NOT won)
);

CREATE INDEX match_payments_by_character ON match_payments (character_id);
