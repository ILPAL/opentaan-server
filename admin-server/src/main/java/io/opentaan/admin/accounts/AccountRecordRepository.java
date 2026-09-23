package io.opentaan.admin.accounts;

import java.util.Optional;

import org.springframework.data.repository.Repository;

/** Read-only access to {@code accounts}; no save or delete methods exist to call. */
interface AccountRecordRepository extends Repository<AccountRecord, Long> {

    Optional<AccountRecord> findById(Long accountId);
}
