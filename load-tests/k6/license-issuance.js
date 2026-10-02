import http from 'k6/http';
import { check } from 'k6';
import { Counter, Rate, Trend } from 'k6/metrics';

export const issuanceDuration = new Trend('symbolon_issuance_duration_ms');
export const totalIssuedLicenses = new Counter('symbolon_issued_licenses_total');
export const issuanceErrorRate = new Rate('symbolon_issuance_error_rate');

export const options = {
  scenarios: {
    issuance_throughput: {
      executor: 'ramping-arrival-rate',
      startRate: 50,
      timeUnit: '1s',
      preAllocatedVUs: 50,
      maxVUs: 300,
      stages: [
        { target: 100, duration: '30s' },
        { target: 200, duration: '1m' }, // Target: >= 200 doc/s
        { target: 250, duration: '30s' },
      ],
    },
  },
  thresholds: {
    // Requirements from §10.9: >= 200 doc/s target, p95 < 100ms
    'symbolon_issuance_duration_ms': ['p(95)<100', 'p(99)<200'],
    'symbolon_issuance_error_rate': ['rate<0.01'],
  },
};

const SERVER_URL = __ENV.SERVER_URL || 'http://localhost:8080';
const ADMIN_TOKEN = __ENV.ADMIN_TOKEN || 'sym_adm_bench_secret_key';

export default function () {
  const id = `lic_perf_${__VU}_${__ITER}_${Date.now()}`;

  const payload = JSON.stringify({
    productId: 'cad-pro-2026',
    policyId: 'floating-standard',
    maxSeats: 25,
    validDays: 365,
    customerEmail: `benchmark_client_${__VU}@example.com`,
  });

  const params = {
    headers: {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${ADMIN_TOKEN}`,
      'X-Api-Key': ADMIN_TOKEN,
    },
  };

  const start = Date.now();
  const res = http.post(`${SERVER_URL}/admin/v1/licenses`, payload, params);
  const duration = Date.now() - start;
  issuanceDuration.add(duration);

  if (res.status === 200 || res.status === 201) {
    totalIssuedLicenses.add(1);
    issuanceErrorRate.add(0);

    const body = res.json();
    check(res, {
      'status is 200/201': (r) => r.status === 200 || r.status === 201,
      'has licenseKey': () => body.licenseKey !== undefined && body.licenseKey !== '',
    });
  } else {
    issuanceErrorRate.add(1);
    check(res, {
      'issuance succeeded': (r) => r.status === 200 || r.status === 201,
    });
  }
}
