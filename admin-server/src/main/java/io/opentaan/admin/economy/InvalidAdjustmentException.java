package io.opentaan.admin.economy;

/** The request breaks a rule the contract cannot express, such as a zero amount. */
public class InvalidAdjustmentException extends RuntimeException {

    public InvalidAdjustmentException(String message) {
        super(message);
    }
}
