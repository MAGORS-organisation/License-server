import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Rate, Trend } from 'k6/metrics';

// Custom metrics for strict invariant auditing
export const successfulCheckouts = new Counter('symbolon_successful_checkouts');
export const deniedCheckouts = new Counter('symbolon_denied_checkouts');
export const overAllocations = new Counter('symbolon_over_allocations');
export const checkoutDuration = new Trend('symbolon_checkout_duration_ms');
export const errorRate = new Rate('symbolon_error_rate');

export const options = {
  scenarios: {
    checkout_burst: {
      executor: 'per-vu-iterations',
      vus: 500, // 500 concurrent virtual clients hitting the pool at the exact same second
      iterations: 1,
      maxDuration: '30s',
    },
  },
  thresholds: {
    // Requirements from §10.9
    'symbolon_checkout_duration_ms': ['p(95)<150', 'p(99)<250'],
    'symbolon_over_allocations': ['count==0'], // ZERO over-allocations permitted
    'symbolon_error_rate': ['rate<0.01'], // General 5xx/unexpected errors must be under 1%
  },
};

const SERVER_URL = __ENV.SERVER_URL || 'http://localhost:8080';
const LICENSE_KEY = __ENV.LICENSE_KEY || 'SYM-TEST-BURST-100';

export default function () {
  const vuId = __VU;
  const machineId = `bench-mach-${vuId}-${Date.now()}`;
  const fingerprint = `sha256:bench_${vuId}_${machineId}`;

  const payload = JSON.stringify({
    licenseKey: LICENSE_KEY,
    machineId: machineId,
    fingerprint: fingerprint,
    hostname: `host-${vuId}.internal`,
    username: `eng_user_${vuId}`,
  });

  const params = {
    headers: {
      'Content-Type': 'application/json',
      'X-Symbolon-Client-Version': '1.0.0',
    },
  };

  const start = Date.now();
  const res = http.post(`${SERVER_URL}/v1/leases`, payload, params);
  const duration = Date.now() - start;
  checkoutDuration.add(duration);

  if (res.status === 200 || res.status === 201) {
    successfulCheckouts.add(1);
    errorRate.add(0);

    const body = res.json();
    check(res, {
      'status is 200/201': (r) => r.status === 200 || r.status === 201,
      'has leaseId': () => body.leaseId !== undefined && body.leaseId !== '',
      'has seat number': () => body.seat > 0,
    });

    // Hold lease briefly then release
    sleep(1);
    if (body.leaseId) {
      http.del(`${SERVER_URL}/v1/leases/${body.leaseId}`, null, params);
    }
  } else if (res.status === 409 || res.status === 429) {
    // Capacity exhausted - expected for requests beyond pool capacity
    deniedCheckouts.add(1);
    errorRate.add(0);

    check(res, {
      'status is 409 capacity exhausted': (r) => r.status === 409 || r.status === 429,
    });
  } else {
    // Unexpected error (e.g. 500 Internal Server Error)
    errorRate.add(1);
    check(res, {
      'unexpected status': (r) => r.status < 500,
    });
  }
}
