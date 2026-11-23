// Load test: the traffic we expect on a normal busy day. Should pass its thresholds.
import { sleep } from 'k6';
import { randomRequest } from './lib.js';
export { setup } from './lib.js';

export const options = {
  stages: [
    { duration: '1m', target: 20 }, // warm up
    { duration: '5m', target: 20 }, // steady normal load
    { duration: '1m', target: 0 }, // cool down
  ],
  thresholds: {
    http_req_failed: ['rate<0.01'], // under 1% errors
    http_req_duration: ['p(95)<500', 'p(99)<1500'],
  },
};

export default function () {
  randomRequest();
  sleep(1); // a user waits about a second between requests
}
