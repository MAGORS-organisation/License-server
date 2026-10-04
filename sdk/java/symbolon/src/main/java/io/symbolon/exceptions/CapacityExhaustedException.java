package io.symbolon.exceptions;

public class CapacityExhaustedException extends SymbolonException {
    public CapacityExhaustedException(String message) {
        super("CAPACITY_EXHAUSTED", 429, message);
    }

    public CapacityExhaustedException(String message, String detail) {
        super("CAPACITY_EXHAUSTED", 429, message, detail);
    }
}
