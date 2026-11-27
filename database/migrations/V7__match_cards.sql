-- The offer each character saw on the result screen, and the card it picked.
-- Offers are kept with the match and its payments. A pick and the progress it grants
-- are then kept in one transaction, so retrying a pick cannot grant it twice.

CREATE TABLE match_cards (
    match_id          UUID     NOT NULL,
    character_id      BIGINT   NOT NULL CHECK (character_id BETWEEN 1 AND 4294967295),
    reward_kind       TEXT     NOT NULL CHECK (reward_kind IN ('gold', 'experience')),
    amount            BIGINT   NOT NULL CHECK (amount BETWEEN 1 AND 4294967295),
    picked_card_index SMALLINT CHECK (picked_card_index BETWEEN 0 AND 7),
    PRIMARY KEY (match_id, character_id),
    FOREIGN KEY (match_id, character_id) REFERENCES match_payments (match_id, character_id),
    CONSTRAINT one_character_per_card UNIQUE (match_id, picked_card_index)
);
