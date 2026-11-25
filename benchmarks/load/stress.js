// Stress test: raises the request rate step by step to find where the server breaks.
// It sends a fixed number of requests per second, whether or not the server keeps up,
// so slow answers show up as rising latency and errors instead of fewer requests.
// Look at which step latency or errors start rising: that is the capacity.
import { randomRequest } from './lib.js';
export { setup } from './lib.js';

const MAX_RATE = Number(__ENV.MAX_RATE || 200); // requests per second at the last step

export const options = {
  scenarios: {
    stress: {
      executor: 'ramping-arrival-rate',
      startRate: 5,
      timeUnit: '1s',
      preAllocatedVUs: 50,
      maxVUs: 500,
      stages: [
        { duration: '2m', target: Math.round(MAX_RATE * 0.25) },
        { duration: '2m', target: Math.round(MAX_RATE * 0.5) },
        { duration: '2m', target: Math.round(MAX_RATE * 0.75) },
        { duration: '2m', target: MAX_RATE },
        { duration: '1m', target: 0 }, // does it recover?
      ],
    },
  },
  thresholds: {
    // Stops the test when more than 20% of requests fail for a while: the limit is
    // found by then, and the server is shared, so no need to keep hammering it.
    http_req_failed: [{ threshold: 'rate<0.2', abortOnFail: true, delayAbortEval: '30s' }],
    http_req_duration: ['p(95)<2000'],
  },
};

export default function () {
  randomRequest();
}
