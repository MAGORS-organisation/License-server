import http from 'k6/http';
import { check, sleep } from 'k6';

// ==============================================================================
// Symbolon High-Concurrency k6 Load Test
// Usage: k6 run deploy/benchmarks/k6-load-test.js
// ==============================================================================

export const options = {
  stages: [
    { duration: '10s', target: 20 },  // Ramp up to 20 virtual users
    { duration: '30s', target: 100 }, // Peak load: 100 concurrent users
    { duration: '10s', target: 0 },   // Ramp down
  ],
  thresholds: {
    http_req_failed: ['rate<0.05'],     // Under 5% error rate
    http_req_duration: ['p(95)<300'],   // 95% of requests must complete under 300ms
    http_req_duration: ['p(99)<600'],   // 99% of requests must complete under 600ms
  },
};

const BASE_URL = __ENV.SYMBOLON_URL || 'http://localhost:8080';
const LICENSE_KEY = __ENV.SYMBOLON_KEY || 'LIC34-LOAD-TEST-DEMO-KEY';

export default function () {
  const workerId = `vu-${__VU}-${__ITER}`;

  // 1. Checkout Lease
  const checkoutPayload = JSON.stringify({
    licenseKey: LICENSE_KEY,
    machineId: `host-${__VU}`,
    fingerprintComponents: {
      client_id: workerId,
      os: 'linux',
      arch: 'x64'
    },
    quantity: 1,
    allowQueue: true
  });

  const params = {
    headers: {
      'Content-Type': 'application/json',
      'X-Symbolon-Client': 'k6-load-generator'
    },
  };

  const checkoutRes = http.post(`${BASE_URL}/v1/leases`, checkoutPayload, params);
  const checkoutOk = check(checkoutRes, {
    'checkout status is 200 or 202': (r) => r.status === 200 || r.status === 202,
  });

  if (checkoutRes.status === 200) {
    const data = JSON.parse(checkoutRes.body);
    const leaseId = data.leaseId;

    sleep(1);

    // 2. Heartbeat / Renew Lease
    const renewPayload = JSON.stringify({
      leaseId: leaseId,
      clientSequence: 1,
      fingerprintComponents: { client_id: workerId }
    });

    const renewRes = http.post(`${BASE_URL}/v1/leases/renew`, renewPayload, params);
    check(renewRes, {
      'renew status is 200': (r) => r.status === 200,
    });

    sleep(1);

    // 3. Release Lease
    const releasePayload = JSON.stringify({
      leaseId: leaseId,
      fingerprintComponents: { client_id: workerId }
    });

    const releaseRes = http.post(`${BASE_URL}/v1/leases/release`, releasePayload, params);
    check(releaseRes, {
      'release status is 200': (r) => r.status === 200,
    });
  }

  // 4. Scrape Prometheus Metrics occasionally
  if (__ITER % 20 === 0) {
    const metricsRes = http.get(`${BASE_URL}/metrics`);
    check(metricsRes, {
      'metrics status is 200': (r) => r.status === 200,
    });
  }

  sleep(0.5);
}
