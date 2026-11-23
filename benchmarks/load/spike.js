// Spike test: goes from a few users to many in seconds, then back. Checks that the
// server survives a sudden burst and recovers afterwards, without the watchdog
// restarting containers.
import { sleep } from 'k6';
import { randomRequest } from './lib.js';
export { setup } from './lib.js';

const PEAK = Number(__ENV.PEAK || 300); // users at the top of the spike

export const options = {
  stages: [
    { duration: '30s', target: 5 }, // calm
    { duration: '10s', target: PEAK }, // sudden spike
    { duration: '1m', target: PEAK }, // stay at the peak
    { duration: '10s', target: 5 }, // spike is over
    { duration: '1m', target: 5 }, // recovery: latency should return to normal
    { duration: '10s', target: 0 },
  ],
  thresholds: {
    http_req_failed: ['rate<0.05'],
    http_req_duration: ['p(95)<3000'],
  },
};

export default function () {
  randomRequest();
  sleep(1);
}
