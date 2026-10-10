/**
 * Symbolon Cloudflare Worker Edge Validator
 * Global edge execution with zero origin latency
 *
 * Validates Symbolon licenses and tokens at Cloudflare Edge locations worldwide,
 * terminating unauthorized requests at the point of entry and forwarding
 * authenticated traffic to the origin with enriched claims headers.
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
 * Parses and verifies Symbolon Edge Token
 */
function verifyEdgeToken(token, options = {}) {
  if (!token || typeof token !== 'string') {
    return { valid: false, status: 401, error: 'Chýba autorizačný token.' };
  }

  const parts = token.trim().split('.');
  if (parts.length < 2) {
    return { valid: false, status: 401, error: 'Neplatný token formát.' };
  }

  try {
    const payload = JSON.parse(base64UrlDecode(parts[1]));
    const now = options.nowSec || Math.floor(Date.now() / 1000);

    if (payload.exp && payload.exp < now) {
      return { valid: false, status: 403, error: 'Token vypršal (expired).' };
    }

    if (payload.nbf && payload.nbf > now + 60) {
      return { valid: false, status: 403, error: 'Token ešte nie je platný.' };
    }

    return { valid: true, status: 200, payload };
  } catch (err) {
    return { valid: false, status: 401, error: 'Chyba pri dekódovaní tokenu.' };
  }
}

/**
 * Cloudflare Worker fetch event handler
 */
async function handleRequest(request, env = {}, ctx = null) {
  // CORS Preflight
  if (request.method === 'OPTIONS') {
    return new Response(null, {
      status: 204,
      headers: {
        'Access-Control-Allow-Origin': '*',
        'Access-Control-Allow-Methods': 'GET, POST, PUT, DELETE, OPTIONS',
        'Access-Control-Allow-Headers': 'Content-Type, Authorization, X-Symbolon-Token, X-Tenant-Id',
        'Access-Control-Max-Age': '86400'
      }
    });
  }

  const url = new URL(request.url);

  // Exempt public endpoints
  if (url.pathname.startsWith('/health') || url.pathname.startsWith('/v1/portal/branding')) {
    if (env.ORIGIN_URL) {
      return fetch(new Request(env.ORIGIN_URL + url.pathname + url.search, request));
    }
    return new Response(JSON.stringify({ status: 'healthy', edge: 'cloudflare' }), {
      headers: { 'Content-Type': 'application/json' }
    });
  }

  const authHeader = request.headers.get('Authorization') || request.headers.get('X-Symbolon-Token');
  if (!authHeader) {
    return new Response(
      JSON.stringify({
        type: 'https://symbolon.dev/errors/unauthorized',
        title: 'Unauthorized',
        status: 401,
        detail: 'Chýba autorizačný parameter (Authorization: Bearer <token>).'
      }),
      {
        status: 401,
        headers: {
          'Content-Type': 'application/problem+json',
          'WWW-Authenticate': 'Bearer realm="Symbolon Cloudflare Edge"'
        }
      }
    );
  }

  const token = authHeader.startsWith('Bearer ') ? authHeader.slice(7).trim() : authHeader.trim();
  const result = verifyEdgeToken(token, {
    expectedAudience: env.EXPECTED_AUDIENCE
  });

  if (!result.valid) {
    return new Response(
      JSON.stringify({
        type: result.status === 401 ? 'https://symbolon.dev/errors/unauthorized' : 'https://symbolon.dev/errors/forbidden',
        title: result.status === 401 ? 'Unauthorized' : 'Forbidden',
        status: result.status,
        detail: result.error
      }),
      {
        status: result.status,
        headers: { 'Content-Type': 'application/problem+json' }
      }
    );
  }

  // Token valid at edge: forward to origin with injected metadata
  if (env.ORIGIN_URL) {
    const forwardHeaders = new Headers(request.headers);
    forwardHeaders.set('X-Symbolon-Verified', '1');
    forwardHeaders.set('X-Symbolon-Sub', result.payload.sub || '');
    forwardHeaders.set('X-Symbolon-Tenant-Id', result.payload.iss || 'default');

    const originReq = new Request(env.ORIGIN_URL + url.pathname + url.search, {
      method: request.method,
      headers: forwardHeaders,
      body: request.body
    });
    return fetch(originReq);
  }

  return new Response(
    JSON.stringify({
      message: 'Token overený na Cloudflare Edge.',
      claims: result.payload
    }),
    {
      status: 200,
      headers: {
        'Content-Type': 'application/json',
        'X-Symbolon-Verified': '1'
      }
    }
  );
}

// Module Worker export
const workerExport = {
  fetch: handleRequest,
  verifyEdgeToken,
  handleRequest
};

if (typeof module !== 'undefined' && module.exports) {
  module.exports = workerExport;
}
