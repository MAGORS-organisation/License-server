const test = require('node:test');
const assert = require('node:assert/strict');
const {
  SymbolonOfflineValidator,
  generateBrowserFingerprint,
  validateLic34Key,
  unwrapPemArmor,
  checkFeatureEntitlement
} = require('../symbolon-validator.js');

// Helper to Base64Url encode string or Uint8Array/ArrayBuffer
function toBase64Url(input) {
  let buf;
  if (typeof input === 'string') {
    buf = Buffer.from(input, 'utf-8');
  } else if (input instanceof Uint8Array) {
    buf = Buffer.from(input);
  } else if (input instanceof ArrayBuffer) {
    buf = Buffer.from(new Uint8Array(input));
  } else {
    buf = Buffer.from(input);
  }
  return buf.toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

// Helper to generate a test ECDSA P-256 key pair and JWS signature
async function createTestKeyAndLicense(options = {}) {
  const keyPair = await crypto.subtle.generateKey(
    { name: 'ECDSA', namedCurve: 'P-256' },
    true,
    ['sign', 'verify']
  );

  const publicJwk = await crypto.subtle.exportKey('jwk', keyPair.publicKey);
  const kid = options.kid || 'key-wasm-test-1';
  publicJwk.kid = kid;
  publicJwk.alg = 'ES256';

  const jwks = { keys: [publicJwk] };

  const nowSec = options.nowSec || Math.floor(Date.now() / 1000);
  const expSec = options.expSec !== undefined ? options.expSec : (nowSec + 30 * 86400);

  const payload = {
    iss: 'symbolon:control-plane',
    sub: 'lic_corp_8892',
    aud: options.audience || 'enterprise-suite',
    jti: 'jti_test_998',
    iat: nowSec - 3600,
    exp: expSec,
    symlic: {
      v: 1,
      profile: 'hybrid-v1',
      requiredAlgs: ['ES256'],
      license: {
        key: 'SYM-WASM-TEST-1234-5678',
        model: options.fingerprint ? 'nodelock' : 'floating',
        state: 'active',
        customer: {
          name: 'ACME Aerospace Ltd',
          ref: 'CUST-AERO-01'
        }
      },
      limits: {
        maxSeats: options.seats || 25,
        seatUnit: 'user'
      },
      entitlements: [
        { code: 'core' },
        { code: '3d-cad' },
        { code: 'stress-analysis' }
      ],
      binding: options.fingerprint ? { fingerprint: options.fingerprint } : null
    }
  };

  const header = { alg: 'ES256', typ: 'JWT', kid };
  const headerB64 = toBase64Url(JSON.stringify(header));
  const payloadB64 = toBase64Url(JSON.stringify(payload));
  const dataToSign = new TextEncoder().encode(`${headerB64}.${payloadB64}`);

  const rawSig = await crypto.subtle.sign(
    { name: 'ECDSA', hash: 'SHA-256' },
    keyPair.privateKey,
    dataToSign
  );

  const sigB64 = toBase64Url(rawSig);
  const jws = `${headerB64}.${payloadB64}.${sigB64}`;

  const pem = `-----BEGIN SYMBOLON LICENSE-----\n${jws}\n-----END SYMBOLON LICENSE-----`;

  return { keyPair, publicJwk, jwks, payload, jws, pem, kid };
}

test('SymbolonOfflineValidator - Validates genuine PEM license offline with WebCrypto', async () => {
  const { pem, jwks } = await createTestKeyAndLicense();
  const validator = new SymbolonOfflineValidator({ jwks, expectedAudience: 'enterprise-suite' });

  const result = await validator.validate(pem);

  assert.equal(result.isValid, true, `Validation failed: ${result.failureReason}`);
  assert.equal(result.customer, 'ACME Aerospace Ltd');
  assert.equal(result.customerRef, 'CUST-AERO-01');
  assert.equal(result.product, 'enterprise-suite');
  assert.equal(result.maxSeats, 25);
  assert.equal(result.isExpired, false);
  assert.ok(result.daysRemaining > 25);
  assert.deepEqual(result.verifiedAlgs, ['ES256']);
  assert.equal(result.hasFeature('3d-cad'), true);
  assert.equal(result.hasFeature('stress-analysis'), true);
  assert.equal(result.hasFeature('non-existent-module'), false);
});

test('SymbolonOfflineValidator - Rejects tampered payload (seat count inflation)', async () => {
  const { jws, jwks } = await createTestKeyAndLicense({ seats: 5 });
  const validator = new SymbolonOfflineValidator({ jwks });

  // Tamper with payload: inflate seats from 5 to 50000
  const parts = jws.split('.');
  const payloadJson = Buffer.from(parts[1], 'base64').toString('utf8');
  const tamperedPayload = JSON.parse(payloadJson);
  tamperedPayload.symlic.limits.maxSeats = 50000;
  const tamperedB64 = toBase64Url(JSON.stringify(tamperedPayload));

  const tamperedJws = `${parts[0]}.${tamperedB64}.${parts[2]}`;

  const result = await validator.validate(tamperedJws);

  assert.equal(result.isValid, false);
  assert.ok(result.failureReason.includes('signature verification failed') || result.failureReason.includes('tampered'));
});

test('SymbolonOfflineValidator - Rejects corrupted signature bytes', async () => {
  const { jws, jwks } = await createTestKeyAndLicense();
  const validator = new SymbolonOfflineValidator({ jwks });

  const parts = jws.split('.');
  // Invert bytes in signature
  const sigBuf = Buffer.from(parts[2], 'base64');
  sigBuf[0] ^= 0xff;
  sigBuf[1] ^= 0xaa;
  const badSig = toBase64Url(sigBuf);

  const corruptedJws = `${parts[0]}.${parts[1]}.${badSig}`;

  const result = await validator.validate(corruptedJws);

  assert.equal(result.isValid, false);
  assert.ok(result.failureReason.includes('signature verification failed') || result.failureReason.includes('tampered'));
});

test('SymbolonOfflineValidator - Rejects license when checked against wrong public key', async () => {
  const { pem } = await createTestKeyAndLicense({ kid: 'legit-key' });

  // Generate completely different key pair
  const rogueKeyPair = await crypto.subtle.generateKey(
    { name: 'ECDSA', namedCurve: 'P-256' },
    true,
    ['sign', 'verify']
  );
  const rogueJwk = await crypto.subtle.exportKey('jwk', rogueKeyPair.publicKey);
  rogueJwk.kid = 'legit-key'; // Same kid, completely different EC key parameters

  const validator = new SymbolonOfflineValidator({ jwks: { keys: [rogueJwk] } });

  const result = await validator.validate(pem);

  assert.equal(result.isValid, false);
  assert.ok(result.failureReason.includes('signature verification failed') || result.failureReason.includes('tampered'));
});

test('SymbolonOfflineValidator - Rejects expired license and reports expiration details', async () => {
  const now = Math.floor(Date.now() / 1000);
  const expiredTimestamp = now - (7 * 86400); // 7 days ago

  const { pem, jwks } = await createTestKeyAndLicense({ expSec: expiredTimestamp });
  const validator = new SymbolonOfflineValidator({ jwks });

  const result = await validator.validate(pem);

  assert.equal(result.isValid, false);
  assert.equal(result.isExpired, true);
  assert.equal(result.daysRemaining, 0);
  assert.ok(result.failureReason.includes('expired'));
  assert.equal(result.customer, 'ACME Aerospace Ltd');
});

test('SymbolonOfflineValidator - Rejects audience mismatch', async () => {
  const { pem, jwks } = await createTestKeyAndLicense({ audience: 'design-cad-app' });
  const validator = new SymbolonOfflineValidator({
    jwks,
    expectedAudience: 'financial-ledger-app' // Expected something else
  });

  const result = await validator.validate(pem);

  assert.equal(result.isValid, false);
  assert.ok(result.failureReason.includes('Audience mismatch'));
});

test('SymbolonOfflineValidator - Node-lock license matches current machine fingerprint', async () => {
  const targetFp = 'fp_web_88f921ab04e28c11927361283921045a';
  const { pem, jwks } = await createTestKeyAndLicense({ fingerprint: targetFp });

  const validator = new SymbolonOfflineValidator({ jwks });

  const result = await validator.validate(pem, targetFp);

  assert.equal(result.isValid, true);
  assert.equal(result.machineMatch, true);
  assert.equal(result.isNodeLocked, true);
  assert.equal(result.boundFingerprint, targetFp);
});

test('SymbolonOfflineValidator - Node-lock license rejects different machine fingerprint', async () => {
  const legitFp = 'fp_web_88f921ab04e28c11927361283921045a';
  const rogueFp = 'fp_web_99999999999999999999999999999999';
  const { pem, jwks } = await createTestKeyAndLicense({ fingerprint: legitFp });

  const validator = new SymbolonOfflineValidator({ jwks });

  const result = await validator.validate(pem, rogueFp);

  assert.equal(result.isValid, false);
  assert.equal(result.machineMatch, false);
  assert.ok(result.failureReason.includes('Hardware fingerprint mismatch'));
});

test('SymbolonOfflineValidator - generateBrowserFingerprint produces consistent prefix and length', async () => {
  const fp1 = await generateBrowserFingerprint();
  const fp2 = await generateBrowserFingerprint();

  assert.ok(fp1.startsWith('fp_web_'), `Expected fp_web_ prefix, got ${fp1}`);
  assert.equal(fp1.length, 39); // 'fp_web_' (7) + 32 hex chars = 39
  assert.equal(fp1, fp2, 'Fingerprint must be deterministic for the same environment');
});

test('SymbolonOfflineValidator - validateLic34Key parses Crockford Base32 product keys', () => {
  // Valid LIC-34 format: 7 groups of 4 + 1 group of 2 Crockford chars
  const validKey = 'ABCD-EFGH-JKMN-PQRT-VWXY-1234-5678-9A';
  assert.equal(validateLic34Key(validKey), true);

  // Invalid length
  assert.equal(validateLic34Key('ABCD-EFGH-1234'), false);

  // Invalid characters (I, O, L, U are prohibited in Crockford Base32)
  assert.equal(validateLic34Key('ABCI-EFGH-JKMN-PQRT-VWXY-1234-5678-9A'), false);
  assert.equal(validateLic34Key('ABCD-EFGO-JKMN-PQRT-VWXY-1234-5678-9A'), false);
});

test('SymbolonOfflineValidator - unwrapPemArmor extracts inner token', () => {
  const rawToken = 'eyJhbGciOiJFUzI1NiJ9.eyJzdWIiOiIxMjMifQ.c2ln';
  const pem = `-----BEGIN SYMBOLON LICENSE-----\n${rawToken}\n-----END SYMBOLON LICENSE-----`;

  assert.equal(unwrapPemArmor(pem), rawToken);
  assert.equal(unwrapPemArmor(rawToken), rawToken);
});

test('checkFeatureEntitlement - Validates features, wildcards and version matches', () => {
  // 1. Direct entitlement match
  const claims1 = { entitlements: ['CAD_3D', 'FEA_SOLVER'] };
  assert.equal(checkFeatureEntitlement(claims1, 'CAD_3D'), true);
  assert.equal(checkFeatureEntitlement(claims1, 'cad_3d'), true);
  assert.equal(checkFeatureEntitlement(claims1, 'CAM_POST'), false);

  // 2. Wildcard all-access match
  const claims2 = { entitlements: ['*'] };
  assert.equal(checkFeatureEntitlement(claims2, 'ANY_FEATURE_XYZ'), true);

  // 3. Feature map with version constraints
  const claims3 = {
    features: ['BASE_UI'],
    feature_map: {
      'fea_solver': '2026.1',
      'cam_post': '*'
    }
  };
  assert.equal(checkFeatureEntitlement(claims3, 'fea_solver', '2026.1'), true);
  assert.equal(checkFeatureEntitlement(claims3, 'fea_solver', '2025.4'), false);
  assert.equal(checkFeatureEntitlement(claims3, 'cam_post', 'any-version'), true);
  assert.equal(checkFeatureEntitlement(claims3, 'non_existent'), false);

  // 4. Null / empty claims handling
  assert.equal(checkFeatureEntitlement(null, 'CAD_3D'), false);
  assert.equal(checkFeatureEntitlement(claims1, null), false);
  assert.equal(checkFeatureEntitlement(claims1, ''), false);
});

