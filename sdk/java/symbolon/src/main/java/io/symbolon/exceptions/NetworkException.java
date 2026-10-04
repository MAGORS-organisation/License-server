package io.symbolon.exceptions;

public class NetworkException extends SymbolonException {
    public NetworkException(String message, Throwable cause) {
        super("NETWORK_ERROR", 0, message, null, cause);
    }
}
