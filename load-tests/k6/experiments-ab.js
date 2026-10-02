import http from 'k6/http';
import { check } from 'k6';
import { Counter, Rate, Trend } from 'k6/metrics';

export const experimentDuration = new Trend('symbolon_experiment_eval_duration_ms');
export const variantControl = new Counter('symbolon_variant_control_total');
export const variantTreatment = new Counter('symbolon_variant_treatment_total');
export const evalErrorRate = new Rate('symbolon_experiment_error_rate');

export const options = {
  scenarios: {
    ab_routing_burst: {
      executor: 'per-vu-iterations',
      vus: 200,
      iterations: 50, // 10,000 total evaluations under load
      maxDuration: '1m',
    },
  },
  thresholds: {
    'symbolon_experiment_eval_duration_ms': ['p(95)<10', 'p(99)<25'],
    'symbolon_experiment_error_rate': ['rate<0.001'],
  },
};

const SERVER_URL = __ENV.SERVER_URL || 'http://localhost:8080';
const EXPERIMENT_ID = __ENV.EXPERIMENT_ID || 'exp_bench_concurrency';

export default function () {
  const vuId = __VU;
  const iterId = __ITER;
  const licenseKey = `SYM-KEY-BENCH-${vuId}-${iterId}`;
  const machineId = `mach-bench-${vuId}-${iterId}`;

  const payload = JSON.stringify({
    experimentId: EXPERIMENT_ID,
    licenseKey: licenseKey,
    machineId: machineId,
  });

  const params = {
    headers: { 'Content-Type': 'application/json' },
  };

  const start = Date.now();
  const res = http.post(`${SERVER_URL}/v1/experiments/evaluate`, payload, params);
  const duration = Date.now() - start;
  experimentDuration.add(duration);

  if (res.status === 200) {
    evalErrorRate.add(0);
    const body = res.json();
    check(res, {
      'status is 200': (r) => r.status === 200,
      'has variantId': () => body.variantId !== undefined,
    });

    if (body.variantId === 'control' || body.variantId === 'A') {
      variantControl.add(1);
    } else {
      variantTreatment.add(1);
    }
  } else {
    evalErrorRate.add(1);
  }
}
