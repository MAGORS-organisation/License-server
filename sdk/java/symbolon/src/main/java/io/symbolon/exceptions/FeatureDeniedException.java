package io.symbolon.exceptions;

public class FeatureDeniedException extends SymbolonException {
    public FeatureDeniedException(String message) {
        super("FEATURE_DENIED", 403, message);
    }

    public FeatureDeniedException(String message, String detail) {
        super("FEATURE_DENIED", 403, message, detail);
    }
}
