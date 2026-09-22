/**
 * Symbolon WebAssembly & In-Browser Offline License Validator SDK
 * High-performance, zero-dependency cryptographic license verification
 * using standard W3C WebCrypto API (NIST P-256 / ES256) and hardware fingerprinting.
 *
 * Conforms to Symbolon Normative Specification (spec/03-symlic-1.md).
 * License: Apache-2.0
 */

(function (root, factory) {
  if (typeof define === 'function' && define.amd) {
    define([], factory);
  } else if (typeof module === 'object' && module.exports) {
    module.exports = factory();
  } else {
    root.SymbolonValidator = factory();
    // Also expose SymbolonOfflineValidator directly on window / root
    root.SymbolonOfflineValidator = root.SymbolonValidator.SymbolonOfflineValidator;
    root.generateBrowserFingerprint = root.SymbolonValidator.generateBrowserFingerprint;
  }
}(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  /**
   * Helper to retrieve WebCrypto API in any environment (Browser, Worker, Node.js >= 16)
   */
  function getWebCrypto() {
    if (typeof globalThis !== 'undefined' && globalThis.crypto && globalThis.crypto.subtle) {
      return globalThis.crypto;
    }
    if (typeof window !== 'undefined' && window.crypto && window.crypto.subtle) {
      return window.crypto;
    }
    if (typeof self !== 'undefined' && self.crypto && self.crypto.subtle) {
      return self.crypto;
    }
    try {
      // Node.js fallback
      const nodeCrypto = require('crypto');
      if (nodeCrypto.webcrypto && nodeCrypto.webcrypto.subtle) {
        return nodeCrypto.webcrypto;
      }
    } catch (_) {
      // Ignore
    }
    throw new Error('WebCrypto API (crypto.subtle) is not supported in this runtime environment.');
  }

  /**
   * Base64Url string to Uint8Array decoder
   */
  function base64UrlToUint8Array(base64Url) {
    let base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/');
    while (base64.length % 4) {
      base64 += '=';
    }
    if (typeof atob === 'function') {
      const binary = atob(base64);
      const bytes = new Uint8Array(binary.length);
      for (let i = 0; i < binary.length; i++) {
        bytes[i] = binary.charCodeAt(i);
      }
      return bytes;
    }
    if (typeof Buffer !== 'undefined') {
      return new Uint8Array(Buffer.from(base64, 'base64'));
    }
    throw new Error('No Base64 decoder available in runtime.');
  }

  /**
   * Uint8Array to Base64Url string encoder
   */
  function uint8ArrayToBase64Url(bytes) {
    let binary = '';
    for (let i = 0; i < bytes.length; i++) {
      binary += String.fromCharCode(bytes[i]);
    }
    let base64 = '';
    if (typeof btoa === 'function') {
      base64 = btoa(binary);
    } else if (typeof Buffer !== 'undefined') {
      base64 = Buffer.from(bytes).toString('base64');
    }
    return base64.replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  }

  /**
   * Base64Url string to UTF-8 string decoder
   */
  function base64UrlToUtf8(base64Url) {
    const bytes = base64UrlToUint8Array(base64Url);
    if (typeof TextDecoder !== 'undefined') {
      return new TextDecoder('utf-8').decode(bytes);
    }
    if (typeof Buffer !== 'undefined') {
      return Buffer.from(bytes).toString('utf8');
    }
    let str = '';
    for (let i = 0; i < bytes.length; i++) {
      str += String.fromCharCode(bytes[i]);
    }
    return decodeURIComponent(escape(str));
  }

  /**
   * String to UTF-8 Uint8Array encoder
   */
  function utf8ToUint8Array(str) {
    if (typeof TextEncoder !== 'undefined') {
      return new TextEncoder().encode(str);
    }
    if (typeof Buffer !== 'undefined') {
      return new Uint8Array(Buffer.from(str, 'utf8'));
    }
    const utf8 = unescape(encodeURIComponent(str));
    const arr = new Uint8Array(utf8.length);
    for (let i = 0; i < utf8.length; i++) {
      arr[i] = utf8.charCodeAt(i);
    }
    return arr;
  }

  /**
   * Unwraps PEM armor envelope from a license document.
   */
  function unwrapPemArmor(text) {
    if (!text || typeof text !== 'string') return '';
    const trimmed = text.trim();
    if (!trimmed.includes('-----BEGIN ')) {
      return trimmed;
    }
    const match = trimmed.match(/-----BEGIN [^-]+-----\r?\n([\s\S]+?)\r?\n-----END [^-]+-----/);
    if (match && match[1]) {
      return match[1].replace(/\s+/g, '');
    }
    return trimmed;
  }

  /**
   * Generates a stable, privacy-preserving browser hardware fingerprint for web node-locking.
   * Computes SHA-256 of Canvas rendering, WebGL renderer, screen properties, and system environment.
   * @returns {Promise<string>} Hexadecimal fingerprint prefixed with 'fp_web_'
   */
  async function generateBrowserFingerprint() {
    const cryptoObj = getWebCrypto();
    const parts = [];

    // 1. Screen & Display
    if (typeof screen !== 'undefined') {
      parts.push(`screen:${screen.width}x${screen.height}x${screen.colorDepth}`);
      parts.push(`avail:${screen.availWidth}x${screen.availHeight}`);
    }

    // 2. Navigator Environment
    if (typeof navigator !== 'undefined') {
      parts.push(`lang:${navigator.language || ''}`);
      parts.push(`hwc:${navigator.hardwareConcurrency || 1}`);
      parts.push(`platform:${navigator.platform || ''}`);
      parts.push(`useragent:${navigator.userAgent || ''}`);
    }

    // 3. Timezone & Locale
    try {
      const tz = Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
      parts.push(`tz:${tz}`);
    } catch (_) {
      parts.push('tz:UTC');
    }

    // 4. Canvas Fingerprint
    try {
      if (typeof document !== 'undefined' && document.createElement) {
        const canvas = document.createElement('canvas');
        canvas.width = 240;
        canvas.height = 60;
        const ctx = canvas.getContext('2d');
        if (ctx) {
          ctx.textBaseline = 'top';
          ctx.font = "14px 'Arial', sans-serif";
          ctx.fillStyle = '#f60';
          ctx.fillRect(125, 1, 62, 20);
          ctx.fillStyle = '#069';
          ctx.fillText('Symbolon-Wasm-NodeLock-2026', 2, 15);
          ctx.fillStyle = 'rgba(102, 204, 0, 0.7)';
          ctx.fillText('Symbolon-Wasm-NodeLock-2026', 4, 17);
          parts.push(`canvas:${canvas.toDataURL()}`);
        }
      }
    } catch (_) {
      // Ignore canvas errors in restricted iframe
    }

    // 5. WebGL Renderer Info
    try {
      if (typeof document !== 'undefined' && document.createElement) {
        const canvas = document.createElement('canvas');
        const gl = canvas.getContext('webgl') || canvas.getContext('experimental-webgl');
        if (gl) {
          const debugInfo = gl.getExtension('WEBGL_debug_renderer_info');
          if (debugInfo) {
            const vendor = gl.getParameter(debugInfo.UNMASKED_VENDOR_WEBGL) || '';
            const renderer = gl.getParameter(debugInfo.UNMASKED_RENDERER_WEBGL) || '';
            parts.push(`webgl:${vendor}~${renderer}`);
          }
        }
      }
    } catch (_) {
      // Ignore WebGL errors
    }

    // Fallback if running outside DOM (e.g. Node.js)
    if (parts.length === 0 && typeof process !== 'undefined') {
      parts.push(`node:${process.platform}:${process.arch}:${process.version}`);
    }

    const rawData = parts.join('||');
    const dataBytes = utf8ToUint8Array(rawData);
    const hashBuffer = await cryptoObj.subtle.digest('SHA-256', dataBytes);
    const hashArray = Array.from(new Uint8Array(hashBuffer));
    const hexHash = hashArray.map(b => b.toString(16).padStart(2, '0')).join('');

    return `fp_web_${hexHash.substring(0, 32)}`;
  }

  /**
   * Validates Crockford Base32 LIC-34 format keys (e.g. ABCD-EFGH-JKMN-PQRT-VWXY-1234-5678-9A).
   */
  function validateLic34Key(keyString) {
    if (!keyString || typeof keyString !== 'string') return false;
    const clean = keyString.trim().toUpperCase();
    const pattern = /^[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{2}$/;
    return pattern.test(clean);
  }

  /**
   * Result of a license validation operation
   */
  class ValidationResult {
    constructor(data = {}) {
      this.isValid = !!data.isValid;
      this.failureReason = data.failureReason || null;
      this.customer = data.customer || null;
      this.customerRef = data.customerRef || null;
      this.product = data.product || null;
      this.licenseType = data.licenseType || null;
      this.licenseKey = data.licenseKey || null;
      this.maxSeats = typeof data.maxSeats === 'number' ? data.maxSeats : null;
      this.expiresAt = typeof data.expiresAt === 'number' ? data.expiresAt : null;
      this.isExpired = !!data.isExpired;
      this.daysRemaining = typeof data.daysRemaining === 'number' ? data.daysRemaining : null;
      this.machineMatch = data.machineMatch !== false;
      this.isNodeLocked = !!data.isNodeLocked;
      this.boundFingerprint = data.boundFingerprint || null;
      this.features = Array.isArray(data.features) ? data.features : [];
      this.verifiedAlgs = Array.isArray(data.verifiedAlgs) ? data.verifiedAlgs : [];
      this.issuer = data.issuer || null;
      this.subject = data.subject || null;
      this.issuedAt = typeof data.issuedAt === 'number' ? data.issuedAt : null;
    }

    /**
     * Checks if a specific feature entitlement is granted by this license.
     * @param {string} featureCode
     * @returns {boolean}
     */
    hasFeature(featureCode) {
      if (!this.isValid || !featureCode) return false;
      return this.features.includes(featureCode.toLowerCase()) || this.features.includes(featureCode);
    }

    /**
     * Formats the expiration timestamp as an ISO-8601 string or 'Never'.
     * @returns {string}
     */
    get expiresAtFormatted() {
      if (!this.expiresAt) return 'Never (Perpetual)';
      return new Date(this.expiresAt * 1000).toISOString();
    }
  }

  /**
   * Symbolon WebAssembly & Web Client Offline License Validator
   */
  class SymbolonOfflineValidator {
    /**
     * @param {Object} options
     * @param {string|Object} [options.jwks] - JSON Web Key Set containing server public keys
     * @param {string} [options.expectedAudience] - Optional expected audience identifier
     * @param {number} [options.clockSkewSeconds=300] - Clock skew tolerance in seconds (default 5 minutes)
     */
    constructor(options = {}) {
      this.jwks = null;
      if (options.jwks) {
        this.jwks = typeof options.jwks === 'string' ? JSON.parse(options.jwks) : options.jwks;
      }
      this.expectedAudience = options.expectedAudience || null;
      this.clockSkewSeconds = typeof options.clockSkewSeconds === 'number' ? options.clockSkewSeconds : 300;
    }

    /**
     * Updates or sets the trusted JWKS public key set.
     * @param {string|Object} jwks
     */
    setJwks(jwks) {
      this.jwks = typeof jwks === 'string' ? JSON.parse(jwks) : jwks;
    }

    /**
     * Fully validates a Symbolon License Document (.symlic / PEM / JWS) offline using WebCrypto.
     * Performs cryptographic ECDSA P-256 signature verification, checks expiration,
     * validates audience/issuer, and verifies hardware fingerprint if node-locked.
     *
     * @param {string} pemOrJws - Raw JWS string, PEM-armored license file content, or LIC-34 key
     * @param {string} [fingerprint] - Local hardware or browser fingerprint for node-lock verification
     * @param {number} [customNowSeconds] - Optional override for current Unix timestamp (for deterministic testing)
     * @returns {Promise<ValidationResult>}
     */
    async validate(pemOrJws, fingerprint = null, customNowSeconds = null) {
      if (!pemOrJws || typeof pemOrJws !== 'string' || !pemOrJws.trim()) {
        return new ValidationResult({
          isValid: false,
          failureReason: 'Missing or empty license document.'
        });
      }

      const nowSec = customNowSeconds ?? Math.floor(Date.now() / 1000);

      // 1. Unwrap PEM armor if present
      let rawContent = unwrapPemArmor(pemOrJws);

      // 2. Check if content is JWS JSON General Serialization or Compact Serialization
      let header = null;
      let payload = null;
      let signatureBase64Url = null;
      let signedDataBytes = null;
      let alg = null;
      let kid = null;

      try {
        if (rawContent.startsWith('{') && rawContent.endsWith('}')) {
          // JWS JSON Serialization
          const jsonObj = JSON.parse(rawContent);
          if (jsonObj.payload && Array.isArray(jsonObj.signatures) && jsonObj.signatures.length > 0) {
            const sigEntry = jsonObj.signatures[0];
            const headerStr = base64UrlToUtf8(sigEntry.protected);
            header = JSON.parse(headerStr);
            payload = JSON.parse(base64UrlToUtf8(jsonObj.payload));
            signatureBase64Url = sigEntry.signature;
            signedDataBytes = utf8ToUint8Array(`${sigEntry.protected}.${jsonObj.payload}`);
            alg = header.alg;
            kid = header.kid;
          } else {
            return new ValidationResult({
              isValid: false,
              failureReason: 'Invalid JWS JSON envelope structure.'
            });
          }
        } else {
          // JWS Compact Serialization: header.payload.signature
          const parts = rawContent.split('.');
          if (parts.length !== 3) {
            return new ValidationResult({
              isValid: false,
              failureReason: `Malformed JWS structure (expected 3 dot-separated parts, got ${parts.length}).`
            });
          }
          header = JSON.parse(base64UrlToUtf8(parts[0]));
          payload = JSON.parse(base64UrlToUtf8(parts[1]));
          signatureBase64Url = parts[2];
          signedDataBytes = utf8ToUint8Array(`${parts[0]}.${parts[1]}`);
          alg = header.alg;
          kid = header.kid;
        }
      } catch (err) {
        return new ValidationResult({
          isValid: false,
          failureReason: `Failed to decode JWS token components: ${err.message}`
        });
      }

      // 3. Cryptographic Signature Verification using WebCrypto
      const verifiedAlgs = [];
      if (this.jwks) {
        try {
          const isSignatureValid = await this._verifySignature(
            header,
            signedDataBytes,
            signatureBase64Url
          );
          if (!isSignatureValid) {
            return new ValidationResult({
              isValid: false,
              failureReason: 'Cryptographic signature verification failed. The license document has been tampered with or corrupted.'
            });
          }
          verifiedAlgs.push(alg || 'ES256');
        } catch (cryptoErr) {
          return new ValidationResult({
            isValid: false,
            failureReason: `Cryptographic verification error: ${cryptoErr.message}`
          });
        }
      } else {
        // Warning: No JWKS provided, running in unverified claims inspection mode
        // For production security, JWKS must be configured
      }

      // 4. Extract claims and evaluate validity
      const symlic = payload.symlic || {};
      const licenseMeta = symlic.license || {};
      const customerMeta = licenseMeta.customer || {};
      const limits = symlic.limits || {};
      const binding = symlic.binding || {};
      const entitlements = Array.isArray(symlic.entitlements)
        ? symlic.entitlements.map(e => (typeof e === 'string' ? e : e.code))
        : [];

      const customer = customerMeta.name || customerMeta.ref || payload.sub || 'Unknown';
      const customerRef = customerMeta.ref || null;
      const product = payload.aud || 'Unknown';
      const licenseType = licenseMeta.model || 'floating';
      const licenseKey = licenseMeta.key || null;
      const maxSeats = typeof limits.maxSeats === 'number' ? limits.maxSeats : null;
      const exp = typeof payload.exp === 'number' ? payload.exp : null;
      const iat = typeof payload.iat === 'number' ? payload.iat : null;
      const nbf = typeof payload.nbf === 'number' ? payload.nbf : null;

      // Check Audience if expected
      if (this.expectedAudience && payload.aud) {
        if (payload.aud !== this.expectedAudience) {
          return new ValidationResult({
            isValid: false,
            failureReason: `Audience mismatch: expected '${this.expectedAudience}', but license was issued for '${payload.aud}'.`,
            customer,
            product,
            licenseType,
            verifiedAlgs
          });
        }
      }

      // Check Not Before / Clock skew
      if (nbf && (nbf - this.clockSkewSeconds) > nowSec) {
        return new ValidationResult({
          isValid: false,
          failureReason: `License is not yet active (not before: ${new Date(nbf * 1000).toISOString()}).`,
          customer,
          product,
          licenseType,
          verifiedAlgs
        });
      }

      // Check Expiration
      const isExpired = exp ? (exp + this.clockSkewSeconds) < nowSec : false;
      const daysRemaining = exp ? Math.max(0, Math.floor((exp - nowSec) / 86400)) : null;

      if (isExpired) {
        return new ValidationResult({
          isValid: false,
          failureReason: `License expired on ${new Date(exp * 1000).toISOString()}.`,
          customer,
          customerRef,
          product,
          licenseType,
          licenseKey,
          maxSeats,
          expiresAt: exp,
          isExpired: true,
          daysRemaining: 0,
          features: entitlements,
          verifiedAlgs,
          issuer: payload.iss,
          subject: payload.sub,
          issuedAt: iat
        });
      }

      // Check Hardware / Browser Node-Lock
      const boundFp = binding.fingerprint || null;
      const isNodeLocked = !!boundFp;
      let machineMatch = true;

      if (isNodeLocked && fingerprint) {
        machineMatch = (fingerprint.trim().toLowerCase() === boundFp.trim().toLowerCase());
        if (!machineMatch) {
          return new ValidationResult({
            isValid: false,
            failureReason: `Hardware fingerprint mismatch. Bound: '${boundFp}', Current: '${fingerprint.trim()}'.`,
            customer,
            customerRef,
            product,
            licenseType,
            licenseKey,
            maxSeats,
            expiresAt: exp,
            isExpired: false,
            daysRemaining,
            machineMatch: false,
            isNodeLocked: true,
            boundFingerprint: boundFp,
            features: entitlements,
            verifiedAlgs,
            issuer: payload.iss,
            subject: payload.sub,
            issuedAt: iat
          });
        }
      }

      return new ValidationResult({
        isValid: true,
        failureReason: null,
        customer,
        customerRef,
        product,
        licenseType,
        licenseKey,
        maxSeats,
        expiresAt: exp,
        isExpired: false,
        daysRemaining,
        machineMatch: true,
        isNodeLocked,
        boundFingerprint: boundFp,
        features: entitlements,
        verifiedAlgs,
        issuer: payload.iss,
        subject: payload.sub,
        issuedAt: iat
      });
    }

    /**
     * Internal signature verifier using WebCrypto ECDSA with SHA-256
     */
    async _verifySignature(header, signedDataBytes, signatureBase64Url) {
      if (!this.jwks || !Array.isArray(this.jwks.keys)) {
        throw new Error('Trusted JWKS must contain a valid "keys" array.');
      }

      const cryptoObj = getWebCrypto();
      const alg = header.alg || 'ES256';
      const kid = header.kid;

      // Find matching key
      let matchedKey = null;
      if (kid) {
        matchedKey = this.jwks.keys.find(k => k.kid === kid);
      }
      if (!matchedKey) {
        matchedKey = this.jwks.keys.find(k => k.kty === 'EC' && k.crv === 'P-256');
      }

      if (!matchedKey) {
        throw new Error(`No compatible verification key found in JWKS for kid: '${kid}' and alg: '${alg}'.`);
      }

      if (matchedKey.kty !== 'EC' || matchedKey.crv !== 'P-256' || !matchedKey.x || !matchedKey.y) {
        throw new Error('Only NIST P-256 (ES256) EC keys are supported for in-browser WebCrypto verification.');
      }

      // Import public EC key into WebCrypto
      const jwkData = {
        kty: 'EC',
        crv: 'P-256',
        x: matchedKey.x,
        y: matchedKey.y,
        ext: true
      };

      const cryptoKey = await cryptoObj.subtle.importKey(
        'jwk',
        jwkData,
        {
          name: 'ECDSA',
          namedCurve: 'P-256'
        },
        false,
        ['verify']
      );

      // In JWS RFC 7515, ES256 signature is raw R || S (64 bytes)
      const rawSigBytes = base64UrlToUint8Array(signatureBase64Url);

      // WebCrypto ECDSA expects the raw 64-byte IEEE P1363 (R || S) format directly
      return await cryptoObj.subtle.verify(
        {
          name: 'ECDSA',
          hash: { name: 'SHA-256' }
        },
        cryptoKey,
        rawSigBytes,
        signedDataBytes
      );
    }
  }

  return {
    SymbolonOfflineValidator,
    ValidationResult,
    generateBrowserFingerprint,
    validateLic34Key,
    unwrapPemArmor
  };
}));
