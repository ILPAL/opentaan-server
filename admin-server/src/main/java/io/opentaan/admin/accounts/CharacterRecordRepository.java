package io.opentaan.admin.accounts;

import java.util.List;

import org.springframework.data.repository.Repository;

/** Read-only access to {@code characters}; no save or delete methods exist to call. */
interface CharacterRecordRepository extends Repository<CharacterRecord, Long> {

    List<CharacterRecord> findByAccountIdOrderBySlotIndexAsc(Long accountId);

    boolean existsById(Long characterId);
}
