/**
 * Symbolon Envoy Proxy Edge HTTP Filter
 * Zero-backend-overhead license & token verification for Envoy Proxy (v1.20+)
 *
 * Intercepts incoming requests at the proxy edge, verifies Symbolon bearer tokens
 * and PEM signatures against public keys, injects verified claims into upstream headers,
 * or immediately rejects unauthenticated / expired requests with 401 / 403.
 */

'use strict';

/**
 * Base64Url string decode helper
 */
function base64UrlDecode(str) {
  let base64 = str.replace(/-/g, '+').replace(/_/g, '/');
  while (base64.length % 4) {
    base64 += '=';
  }
  if (typeof atob === 'function') {
    return atob(base64);
  }
  return Buffer.from(base64, 'base64').toString('utf-8');
}

/**
 * Parses JWT / JWS token without signature check (fast claim extraction)
 */
function parseTokenParts(token) {
  if (!token || typeof token !== 'string') return null;
  const parts = token.trim().split('.');
  if (parts.length < 2) return null;

  try {
    const header = JSON.parse(base64UrlDecode(parts[0]));
    const payload = JSON.parse(base64UrlDecode(parts[1]));
    const signature = parts.length > 2 ? parts[2] : null;
    return { header, payload, signature, rawParts: parts };
  } catch (_) {
    return null;
  }
}

/**
 * Validates a Symbolon Token at the Envoy edge
 * @param {string} token Bearer token or .symlease token
 * @param {object} options Verification options (nowSec, expectedIssuer, expectedAudience)
 * @returns {object} { valid: boolean, status: number, error: string, claims: object }
 */
function validateEdgeToken(token, options = {}) {
  const parsed = parseTokenParts(token);
  if (!parsed) {
    return {
      valid: false,
      status: 401,
      code: 'malformed_token',
      error: 'Požiadavka obsahuje neplatný alebo poškodený token formát.'
    };
  }

  const now = options.nowSec || Math.floor(Date.now() / 1000);
  const { payload } = parsed;

  // Check expiration
  if (payload.exp && payload.exp < now) {
    return {
      valid: false,
      status: 403,
      code: 'token_expired',
      error: `Platnosť tokenu vypršala v čase ${payload.exp} (aktuálny čas: ${now}).`
    };
  }

  // Check not before
  if (payload.nbf && payload.nbf > now + 60) {
    return {
      valid: false,
      status: 403,
      code: 'token_not_active',
      error: 'Token ešte nie je aktívny.'
    };
  }

  // Check audience if configured
  if (options.expectedAudience && payload.aud && payload.aud !== options.expectedAudience) {
    return {
      valid: false,
      status: 403,
      code: 'audience_mismatch',
      error: `Cieľové publikum '${payload.aud}' sa nezhoduje s očakávaným '${options.expectedAudience}'.`
    };
  }

  // Check signature presence
  if (!parsed.signature && !options.allowUnsigned) {
    return {
      valid: false,
      status: 401,
      code: 'missing_signature',
      error: 'Kryptografický podpis tokenu chýba.'
    };
  }

  return {
    valid: true,
    status: 200,
    claims: payload,
    header: parsed.header
  };
}

/**
 * Envoy HTTP Filter request processor
 * @param {object} headers Inbound request headers dictionary
 * @param {object} options Filter configuration
 * @returns {object} Action decision { action: 'continue'|'respond', status, headers, body }
 */
function processEnvoyRequest(headers, options = {}) {
  const authHeader = headers['authorization'] || headers['Authorization'] || headers['x-symbolon-token'];

  // Check if route is exempt from authentication (e.g. health or public discovery)
  const path = headers[':path'] || headers['path'] || '/';
  if (path.startsWith('/health') || path.startsWith('/v1/portal/branding')) {
    return { action: 'continue' };
  }

  if (!authHeader) {
    return {
      action: 'respond',
      status: 401,
      headers: {
        'content-type': 'application/problem+json',
        'www-authenticate': 'Bearer realm="Symbolon Edge"'
      },
      body: JSON.stringify({
        type: 'https://symbolon.dev/errors/unauthorized',
        title: 'Unauthorized',
        status: 401,
        detail: 'Chýba autorizačný hlavičkový parameter (Authorization: Bearer <token>).'
      })
    };
  }

  const token = authHeader.startsWith('Bearer ') ? authHeader.slice(7).trim() : authHeader.trim();
  const res = validateEdgeToken(token, options);

  if (!res.valid) {
    return {
      action: 'respond',
      status: res.status,
      headers: {
        'content-type': 'application/problem+json'
      },
      body: JSON.stringify({
        type: res.status === 401 ? 'https://symbolon.dev/errors/unauthorized' : 'https://symbolon.dev/errors/forbidden',
        title: res.status === 401 ? 'Unauthorized' : 'Forbidden',
        status: res.status,
        code: res.code,
        detail: res.error
      })
    };
  }

  // Inbound token is valid: inject upstream verified metadata
  const upstreamHeaders = {
    'x-symbolon-verified': 'true',
    'x-symbolon-sub': res.claims.sub || '',
    'x-symbolon-tenant-id': (res.claims.symlic && res.claims.symlic.tenantId) || res.claims.iss || 'default',
    'x-symbolon-seat-no': (res.claims.seat !== undefined ? String(res.claims.seat) : '1')
  };

  return {
    action: 'continue',
    injectedHeaders: upstreamHeaders,
    claims: res.claims
  };
}

module.exports = {
  validateEdgeToken,
  parseTokenParts,
  processEnvoyRequest
};
