package io.symbolon.exceptions;

/**
 * Base exception for all Symbolon SDK errors.
 */
public class SymbolonException extends RuntimeException {
    private final String errorCode;
    private final int statusCode;
    private final String detail;

    public SymbolonException(String message) {
        this("UNKNOWN_ERROR", 0, message, null, null);
    }

    public SymbolonException(String errorCode, String message) {
        this(errorCode, 0, message, null, null);
    }

    public SymbolonException(String errorCode, int statusCode, String message) {
        this(errorCode, statusCode, message, null, null);
    }

    public SymbolonException(String errorCode, int statusCode, String message, String detail) {
        this(errorCode, statusCode, message, detail, null);
    }

    public SymbolonException(String errorCode, int statusCode, String message, String detail, Throwable cause) {
        super(formatMessage(errorCode, statusCode, message), cause);
        this.errorCode = errorCode;
        this.statusCode = statusCode;
        this.detail = detail;
    }

    private static String formatMessage(String errorCode, int statusCode, String message) {
        if (statusCode > 0) {
            return String.format("Symbolon [%s] HTTP %d: %s", errorCode, statusCode, message);
        }
        return String.format("Symbolon [%s]: %s", errorCode, message);
    }

    public String getErrorCode() {
        return errorCode;
    }

    public int getStatusCode() {
        return statusCode;
    }

    public String getDetail() {
        return detail;
    }
}
