// Soak test: moderate load for a long time. Finds problems that only show up over
// time, like memory growth, MySQL ending up in swap, or connections that are not freed.
// Compare latency at the start and the end: it should stay flat.
import { sleep } from 'k6';
import { randomRequest } from './lib.js';
export { setup } from './lib.js';

const DURATION = __ENV.DURATION || '30m';
const USERS = Number(__ENV.USERS || 15);

export const options = {
  stages: [
    { duration: '2m', target: USERS },
    { duration: DURATION, target: USERS },
    { duration: '1m', target: 0 },
  ],
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<500'],
  },
};

export default function () {
  randomRequest();
  sleep(1);
}
