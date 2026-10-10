const test = require('node:test');
const assert = require('node:assert/strict');
const { processEnvoyRequest, validateEdgeToken } = require('../envoy/envoy-symbolon-filter.js');
const { verifyEdgeToken, handleRequest } = require('../cloudflare/worker.js');

function toBase64Url(obj) {
  const str = typeof obj === 'string' ? obj : JSON.stringify(obj);
  return Buffer.from(str).toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function createSampleToken(payloadOverrides = {}) {
  const header = { alg: 'ES256', typ: 'JWT', kid: 'edge-key-1' };
  const now = Math.floor(Date.now() / 1000);
  const payload = {
    iss: 'ten_enterprise_01',
    sub: 'lic_8892',
    aud: 'enterprise-suite',
    iat: now - 60,
    exp: now + 3600,
    seat: 5,
    symlic: { tenantId: 'ten_enterprise_01' },
    ...payloadOverrides
  };
  const dummySig = 'MEQCIAz7x9LdummySignatureBytes7893452345';
  return `${toBase64Url(header)}.${toBase64Url(payload)}.${toBase64Url(dummySig)}`;
}

test('Envoy Edge Filter - Passes valid token and injects upstream headers', () => {
  const token = createSampleToken();
  const headers = {
    ':path': '/api/v1/workload',
    authorization: `Bearer ${token}`
  };

  const decision = processEnvoyRequest(headers, { expectedAudience: 'enterprise-suite' });
  assert.equal(decision.action, 'continue');
  assert.ok(decision.injectedHeaders);
  assert.equal(decision.injectedHeaders['x-symbolon-verified'], 'true');
  assert.equal(decision.injectedHeaders['x-symbolon-tenant-id'], 'ten_enterprise_01');
  assert.equal(decision.injectedHeaders['x-symbolon-seat-no'], '5');
});

test('Envoy Edge Filter - Rejects request missing Authorization header with 401', () => {
  const headers = {
    ':path': '/api/v1/workload'
  };

  const decision = processEnvoyRequest(headers);
  assert.equal(decision.action, 'respond');
  assert.equal(decision.status, 401);
  assert.ok(decision.body.includes('Chýba autorizačný'));
});

test('Envoy Edge Filter - Rejects expired token at edge with 403', () => {
  const now = Math.floor(Date.now() / 1000);
  const expiredToken = createSampleToken({ exp: now - 300 });
  const headers = {
    ':path': '/api/v1/workload',
    authorization: `Bearer ${expiredToken}`
  };

  const decision = processEnvoyRequest(headers);
  assert.equal(decision.action, 'respond');
  assert.equal(decision.status, 403);
  assert.ok(decision.body.includes('token_expired'));
});

test('Envoy Edge Filter - Allows exempt public routes without token', () => {
  const headers = {
    ':path': '/health'
  };

  const decision = processEnvoyRequest(headers);
  assert.equal(decision.action, 'continue');
});

test('Cloudflare Worker - Validates token and responds at edge', async () => {
  const token = createSampleToken();
  const request = new Request('https://api.example.com/v1/secure-data', {
    method: 'GET',
    headers: {
      Authorization: `Bearer ${token}`
    }
  });

  const response = await handleRequest(request, { EXPECTED_AUDIENCE: 'enterprise-suite' });
  assert.equal(response.status, 200);
  assert.equal(response.headers.get('X-Symbolon-Verified'), '1');

  const json = await response.json();
  assert.equal(json.claims.sub, 'lic_8892');
  assert.equal(json.claims.seat, 5);
});

test('Cloudflare Worker - Returns 401 when Authorization header is absent', async () => {
  const request = new Request('https://api.example.com/v1/secure-data', {
    method: 'GET',
    headers: {}
  });

  const response = await handleRequest(request);
  assert.equal(response.status, 401);
});

test('Cloudflare Worker - Handles CORS preflight', async () => {
  const request = new Request('https://api.example.com/v1/secure-data', {
    method: 'OPTIONS',
    headers: {
      Origin: 'https://app.client.com'
    }
  });

  const response = await handleRequest(request);
  assert.equal(response.status, 204);
  assert.equal(response.headers.get('Access-Control-Allow-Origin'), '*');
});
