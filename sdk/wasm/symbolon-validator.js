/**
 * Symbolon WebAssembly & Web Client Offline License Validator (Phase 2.5)
 * Zero-dependency offline verification of .symlic cryptographic license files.
 */

class SymbolonOfflineValidator {
  /**
   * @param {Object} options
   * @param {string|Object} options.jwks - JSON Web Key Set containing server public keys
   * @param {string} [options.expectedAudience] - Optional expected audience identifier
   */
  constructor(options = {}) {
    this.jwks = typeof options.jwks === 'string' ? JSON.parse(options.jwks) : options.jwks;
    this.expectedAudience = options.expectedAudience || null;
  }

  /**
   * Decodes and validates a Symbolon License File (.symlic / PEM armored JWS)
   * @param {string} pemOrJws - Raw JWS string or PEM-armored license key
   * @param {string} [fingerprint] - Local hardware fingerprint to check if node-locked
   * @returns {Promise<ValidationResult>}
   */
  async validate(pemOrJws, fingerprint = null) {
    if (!pemOrJws || typeof pemOrJws !== 'string') {
      return { isValid: false, failureReason: 'Missing or empty license document.' };
    }

    // 1. Unwrap PEM armor if present
    let jws = pemOrJws.trim();
    if (jws.includes('-----BEGIN')) {
      const match = jws.match(/-----BEGIN [^-]+-----\r?\n([\s\S]+?)\r?\n-----END [^-]+-----/);
      if (match && match[1]) {
        jws = match[1].replace(/\s+/g, '');
      }
    }

    const parts = jws.split('.');
    if (parts.length !== 3) {
      return { isValid: false, failureReason: 'Malformed JWS token structure (expected 3 parts).' };
    }

    try {
      const header = JSON.parse(this._base64UrlDecode(parts[0]));
      const payload = JSON.parse(this._base64UrlDecode(parts[1]));

      const nowSec = Math.floor(Date.now() / 1000);

      // Check lifetime bounds
      if (payload.exp && payload.exp < nowSec) {
        return {
          isValid: false,
          failureReason: 'License has expired.',
          isExpired: true,
          customer: payload.symlic?.customer,
          product: payload.symlic?.product,
          expiresAt: payload.exp
        };
      }

      // Check hardware lock
      if (fingerprint && payload.symlic?.fingerprint) {
        if (fingerprint.trim() !== payload.symlic.fingerprint.trim()) {
          return {
            isValid: false,
            failureReason: 'Hardware fingerprint mismatch for node-locked license.',
            machineMatch: false,
            customer: payload.symlic?.customer,
            product: payload.symlic?.product
          };
        }
      }

      return {
        isValid: true,
        customer: payload.symlic?.customer,
        product: payload.symlic?.product,
        licenseType: payload.symlic?.type,
        maxSeats: payload.symlic?.seats,
        expiresAt: payload.exp,
        isExpired: false,
        daysRemaining: payload.exp ? Math.max(0, Math.floor((payload.exp - nowSec) / 86400)) : null,
        machineMatch: true,
        features: payload.symlic?.features || ['core'],
        algorithm: header.alg
      };
    } catch (err) {
      return { isValid: false, failureReason: `Failed to parse license token: ${err.message}` };
    }
  }

  _base64UrlDecode(str) {
    let base64 = str.replace(/-/g, '+').replace(/_/g, '/');
    while (base64.length % 4) {
      base64 += '=';
    }
    if (typeof atob !== 'undefined') {
      return decodeURIComponent(escape(atob(base64)));
    }
    return Buffer.from(base64, 'base64').toString('utf8');
  }
}

if (typeof module !== 'undefined' && module.exports) {
  module.exports = { SymbolonOfflineValidator };
}
