package io.symbolon.exceptions;

public class LicenseNotFoundException extends SymbolonException {
    public LicenseNotFoundException(String message) {
        super("LICENSE_NOT_FOUND", 404, message);
    }

    public LicenseNotFoundException(String message, String detail) {
        super("LICENSE_NOT_FOUND", 404, message, detail);
    }
}
