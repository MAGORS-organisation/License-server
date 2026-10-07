import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Rate, Trend } from 'k6/metrics';

// Metrics for Relay seat contention and operational probing (§11.1, §11.2)
export const successfulCheckouts = new Counter('symbolon_relay_successful_checkouts');
export const deniedCheckouts = new Counter('symbolon_relay_denied_checkouts');
export const checkoutDuration = new Trend('symbolon_relay_checkout_duration_ms');
export const healthProbeDuration = new Trend('symbolon_relay_health_probe_duration_ms');
export const errorRate = new Rate('symbolon_relay_error_rate');

export const options = {
  scenarios: {
    relay_checkout_stress: {
      executor: 'ramping-vus',
      startVUs: 10,
      stages: [
        { duration: '10s', target: 100 }, // ramp-up to 100 concurrent clients
        { duration: '20s', target: 100 }, // hold at 100 concurrent clients
        { duration: '5s', target: 0 },    // ramp-down
      ],
      gracefulRampDown: '5s',
    },
    relay_health_monitor: {
      executor: 'constant-arrival-rate',
      rate: 10, // 10 probes/sec
      timeUnit: '1s',
      duration: '35s',
      preAllocatedVUs: 5,
      maxVUs: 20,
    },
  },
  thresholds: {
    'symbolon_relay_checkout_duration_ms': ['p(95)<100', 'p(99)<200'],
    'symbolon_relay_health_probe_duration_ms': ['p(99)<25'],
    'symbolon_relay_error_rate': ['rate<0.01'],
  },
};

const RELAY_URL = __ENV.RELAY_URL || 'http://localhost:5001';
const LICENSE_KEY = __ENV.LICENSE_KEY || 'SYM-AIRGAP-RELAY-01';

export default function () {
  const vuId = __VU;
  const iterId = __ITER;

  // Interleave lease checkout with health probe based on scenario
  if (__ENV.SCENARIO === 'relay_health_monitor' || iterId % 5 === 0) {
    const probeStart = Date.now();
    const grantHealthRes = http.get(`${RELAY_URL}/health/grant`);
    healthProbeDuration.add(Date.now() - probeStart);

    check(grantHealthRes, {
      'grant health status is 200': (r) => r.status === 200,
      'has grant health payload': (r) => {
        try {
          const body = r.json();
          return body.status !== undefined;
        } catch {
          return false;
        }
      },
    });
    return;
  }

  // Seat checkout against local SQLite relay store
  const machineId = `relay-client-${vuId}-${Date.now()}`;
  const payload = JSON.stringify({
    licenseKey: LICENSE_KEY,
    machineId: machineId,
    fingerprint: `sha256:relay_fp_${vuId}_${machineId}`,
    hostname: `workstation-${vuId}.local`,
    username: `eng_${vuId}`,
  });

  const params = {
    headers: {
      'Content-Type': 'application/json',
      'X-Symbolon-Client-Version': '1.0.0',
    },
  };

  const start = Date.now();
  const res = http.post(`${RELAY_URL}/v1/leases`, payload, params);
  checkoutDuration.add(Date.now() - start);

  if (res.status === 200 || res.status === 201) {
    successfulCheckouts.add(1);
    errorRate.add(0);

    const body = res.json();
    check(res, {
      'status is 200/201': (r) => r.status === 200 || r.status === 201,
      'has leaseId': () => body.leaseId !== undefined,
    });

    sleep(0.5);

    // Release seat back to relay pool
    if (body.leaseId) {
      http.del(`${RELAY_URL}/v1/leases/${body.leaseId}`, null, params);
    }
  } else if (res.status === 409 || res.status === 429) {
    // Capacity exhausted or queue wait
    deniedCheckouts.add(1);
    errorRate.add(0);
    check(res, {
      'status is 409 capacity exhausted': (r) => r.status === 409 || r.status === 429,
    });
  } else {
    errorRate.add(1);
    check(res, {
      'status is unexpected error': (r) => r.status < 500,
    });
  }
}
