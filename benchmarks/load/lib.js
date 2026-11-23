// Shared request mix for all load tests. Only GET requests, so the tests never change
// data on the server. Weights roughly follow real use: many lookups of one record,
// fewer full lists, and only a few of the heaviest list (all shipments).
import http from 'k6/http';
import { check, fail } from 'k6';

export const BASE_URL = __ENV.BASE_URL || 'http://145.24.237.19';
const API_KEY = __ENV.API_KEY;

// Record counts from Data/MySql/manifest.json, used to pick random ids.
const COUNTS = {
  warehouses: 10, clients: 300, suppliers: 21, locations: 400, items: 600,
  orders: 4854, shipments: 6132, transfers: 800,
};

// A 404 for an id that does not exist is a correct answer, not a server failure.
http.setResponseCallback(http.expectedStatuses(200, 404));

const id = (name) => Math.floor(Math.random() * COUNTS[name]) + 1;

// [weight, name, path]. The name groups the results per endpoint, not per id.
const REQUESTS = [
  [20, 'GET /items/{id}', () => `/items/${id('items')}`],
  [15, 'GET /orders/{id}', () => `/orders/${id('orders')}`],
  [10, 'GET /shipments/{id}', () => `/shipments/${id('shipments')}`],
  [10, 'GET /orders/{id}/items', () => `/orders/${id('orders')}/items`],
  [8, 'GET /items/{id}/inventory/totals', () => `/items/${id('items')}/inventory/totals`],
  [8, 'GET /clients/{id}', () => `/clients/${id('clients')}`],
  [5, 'GET /warehouses/{id}/locations', () => `/warehouses/${id('warehouses')}/locations`],
  [5, 'GET /transfers/{id}/items', () => `/transfers/${id('transfers')}/items`],
  [5, 'GET /warehouses', () => '/warehouses'],
  [4, 'GET /suppliers', () => '/suppliers'],
  [4, 'GET /items', () => '/items'],
  [3, 'GET /locations', () => '/locations'],
  [2, 'GET /orders', () => '/orders'],
  [1, 'GET /shipments', () => '/shipments'],
];
const TOTAL_WEIGHT = REQUESTS.reduce((sum, [w]) => sum + w, 0);

function pickRequest() {
  let r = Math.random() * TOTAL_WEIGHT;
  for (const req of REQUESTS) {
    r -= req[0];
    if (r < 0) return req;
  }
  return REQUESTS[0];
}

// Runs once before the test: stops right away on a missing or wrong key, instead of
// sending thousands of 401s to the server.
export function setup() {
  if (!API_KEY) fail('Set the API key: k6 run -e API_KEY=... <script>.js');
  const res = http.get(`${BASE_URL}/api/v2/accounts/me`, { headers: { API_KEY } });
  if (res.status !== 200) fail(`API key check failed: /api/v2/accounts/me returned ${res.status}`);
}

// Sends one request from the mix.
export function randomRequest() {
  const [, name, path] = pickRequest();
  const res = http.get(`${BASE_URL}/api/v2${path()}`, {
    headers: { API_KEY },
    tags: { name },
    timeout: '15s', // longer than nginx's 10s, so nginx's 504 shows up instead of a k6 timeout
  });
  check(res, { 'status is 200 or 404': (r) => r.status === 200 || r.status === 404 });
}
