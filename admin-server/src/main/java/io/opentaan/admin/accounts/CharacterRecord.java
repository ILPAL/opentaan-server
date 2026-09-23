package io.opentaan.admin.accounts;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import org.hibernate.annotations.Immutable;

/** A row of {@code characters}, read-only. Only the columns the admin API shows are mapped. */
@Entity
@Immutable
@Table(name = "characters")
public class CharacterRecord {

    @Id
    @Column(name = "character_id")
    private Long characterId;

    @Column(name = "account_id", nullable = false)
    private Long accountId;

    @Column(name = "slot_index", nullable = false)
    private Long slotIndex;

    @Column(name = "name", nullable = false)
    private String name;

    protected CharacterRecord() {
    }

    public Long getCharacterId() {
        return characterId;
    }

    public Long getAccountId() {
        return accountId;
    }

    public Long getSlotIndex() {
        return slotIndex;
    }

    public String getName() {
        return name;
    }
}
