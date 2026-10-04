package io.symbolon.exceptions;

public class LeaseExpiredException extends SymbolonException {
    public LeaseExpiredException(String message) {
        super("LEASE_EXPIRED", 410, message);
    }

    public LeaseExpiredException(String message, String detail) {
        super("LEASE_EXPIRED", 410, message, detail);
    }
}
