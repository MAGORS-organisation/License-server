import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Rate, Trend } from 'k6/metrics';

export const heartbeatDuration = new Trend('symbolon_heartbeat_duration_ms');
export const successfulHeartbeats = new Counter('symbolon_heartbeat_success_total');
export const failedHeartbeats = new Counter('symbolon_heartbeat_failed_total');
export const heartbeatErrorRate = new Rate('symbolon_heartbeat_error_rate');

export const options = {
  scenarios: {
    steady_state_heartbeats: {
      executor: 'constant-arrival-rate',
      rate: 100, // 100 heartbeats per second (exceeds 5000 clients / 2 min = ~42 rps requirement)
      timeUnit: '1s',
      duration: '2m',
      preAllocatedVUs: 50,
      maxVUs: 200,
    },
  },
  thresholds: {
    // Requirements from §10.9: p99 < 50ms, failure threshold p99 > 150ms
    'symbolon_heartbeat_duration_ms': ['p(95)<30', 'p(99)<50'],
    'symbolon_heartbeat_error_rate': ['rate<0.001'], // 99.9% success
  },
};

const SERVER_URL = __ENV.SERVER_URL || 'http://localhost:8080';
const LEASE_ID = __ENV.LEASE_ID || 'les_bench_heartbeat_01';

export default function () {
  const vuId = __VU;
  const seq = __ITER + 1;

  const payload = JSON.stringify({
    seq: seq,
    timestamp: new Date().toISOString(),
  });

  const params = {
    headers: {
      'Content-Type': 'application/json',
      'X-Symbolon-Client-Version': '1.0.0',
    },
  };

  const start = Date.now();
  const res = http.put(`${SERVER_URL}/v1/leases/${LEASE_ID}`, payload, params);
  const duration = Date.now() - start;
  heartbeatDuration.add(duration);

  if (res.status === 200) {
    successfulHeartbeats.add(1);
    heartbeatErrorRate.add(0);

    const body = res.json();
    check(res, {
      'status is 200': (r) => r.status === 200,
      'has renewed expiration': () => body.expiresAt !== undefined,
    });
  } else {
    failedHeartbeats.add(1);
    heartbeatErrorRate.add(1);

    check(res, {
      'heartbeat succeeded': (r) => r.status === 200,
    });
  }
}
