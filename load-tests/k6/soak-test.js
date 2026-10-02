import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Rate, Trend } from 'k6/metrics';

export const soakOperations = new Counter('symbolon_soak_operations_total');
export const soakErrors = new Rate('symbolon_soak_error_rate');
export const soakDuration = new Trend('symbolon_soak_op_duration_ms');

export const options = {
  stages: [
    { duration: '2m', target: 50 },  // Ramp-up
    { duration: '10m', target: 100 }, // Steady soak state (configured to 10m for test run, scalable to 24h)
    { duration: '2m', target: 0 },   // Ramp-down
  ],
  thresholds: {
    'symbolon_soak_error_rate': ['rate<0.005'], // < 0.5% error rate
    'symbolon_soak_op_duration_ms': ['p(95)<100', 'p(99)<250'],
  },
};

const SERVER_URL = __ENV.SERVER_URL || 'http://localhost:8080';
const LICENSE_KEY = __ENV.LICENSE_KEY || 'SYM-TEST-SOAK-100';

export default function () {
  const vuId = __VU;
  const machId = `soak-node-${vuId}`;

  // 1. Checkout
  const checkoutPayload = JSON.stringify({
    licenseKey: LICENSE_KEY,
    machineId: machId,
    fingerprint: `sha256:soak_${vuId}`,
  });

  const headers = { 'Content-Type': 'application/json' };

  const start = Date.now();
  const resCheckout = http.post(`${SERVER_URL}/v1/leases`, checkoutPayload, { headers });
  soakDuration.add(Date.now() - start);
  soakOperations.add(1);

  if (resCheckout.status === 200 || resCheckout.status === 201) {
    soakErrors.add(0);
    const body = resCheckout.json();
    const leaseId = body.leaseId;

    // 2. Heartbeat after 2 seconds
    sleep(2);
    if (leaseId) {
      const hbRes = http.put(`${SERVER_URL}/v1/leases/${leaseId}`, JSON.stringify({ seq: 2 }), { headers });
      soakOperations.add(1);
      soakErrors.add(hbRes.status === 200 ? 0 : 1);

      // 3. Release
      sleep(1);
      const relRes = http.del(`${SERVER_URL}/v1/leases/${leaseId}`, null, { headers });
      soakOperations.add(1);
      soakErrors.add(relRes.status === 200 ? 0 : 1);
    }
  } else if (resCheckout.status === 409) {
    // Capacity reached gracefully
    soakErrors.add(0);
  } else {
    soakErrors.add(1);
  }

  sleep(1);
}
