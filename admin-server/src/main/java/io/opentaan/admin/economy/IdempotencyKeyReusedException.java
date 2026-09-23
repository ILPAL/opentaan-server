package io.opentaan.admin.economy;

/** The idempotency key was used before for a different request. */
public class IdempotencyKeyReusedException extends RuntimeException {

    public IdempotencyKeyReusedException() {
        super("This Idempotency-Key was already used for a different adjustment; use a new key");
    }
}
