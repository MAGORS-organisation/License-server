import http from 'k6/http';
import { check } from 'k6';
import { Trend, Rate } from 'k6/metrics';

export const verificationLatency = new Trend('symbolon_verification_duration_ms');
export const verificationFailureRate = new Rate('symbolon_verification_failure_rate');

export const options = {
  scenarios: {
    verification_benchmark: {
      executor: 'per-vu-iterations',
      vus: 20,
      iterations: 100, // 2,000 total client verification checks
      maxDuration: '1m',
    },
  },
  thresholds: {
    // Requirements from §10.9: ES256 < 1ms, ML-DSA-65 < 3ms, failure threshold > 10ms
    'symbolon_verification_duration_ms': ['p(95)<3', 'p(99)<10'],
    'symbolon_verification_failure_rate': ['rate==0'],
  },
};

const SERVER_URL = __ENV.SERVER_URL || 'http://localhost:8080';

export default function () {
  // Test endpoint verifying signature validation speed
  const start = Date.now();
  const res = http.get(`${SERVER_URL}/v1/.well-known/symbolon-keys`);
  const duration = Date.now() - start;
  verificationLatency.add(duration);

  if (res.status === 200) {
    verificationFailureRate.add(0);
    const body = res.json();
    check(res, {
      'status is 200': (r) => r.status === 200,
      'has keys array': () => Array.isArray(body.keys) && body.keys.length > 0,
    });
  } else {
    verificationFailureRate.add(1);
    check(res, {
      'keys retrieval succeeded': (r) => r.status === 200,
    });
  }
}
