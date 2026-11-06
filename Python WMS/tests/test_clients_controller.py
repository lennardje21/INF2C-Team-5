"""Python version of ClientsControllerTests.cs; runs against v1 or v2."""

import time
from datetime import datetime, timedelta, timezone

from support import ApiTestCase, parse_time, utc_now


def new_client(name="Jumbo Test"):
    return {
        "name": name,
        "address": "Teststraat 1",
        "city": "Rotterdam",
        "zip_code": "3011AA",
        "province": "Zuid-Holland",
        "country": "Netherlands",
        "contact_name": "Test Persoon",
        "contact_phone": "010-1234567",
        "contact_email": "test@example.com",
    }


class ClientsControllerTests(ApiTestCase):
    url = "/api/v1/clients"
    file = "client"

    def test_GetClients_ReturnsSeededClients(self):
        clients = self.get_json(self.url)

        self.assertGreaterEqual(len(clients), 300)
        self.assertEqual("Jumbo Amersfoort Leusderweg", next(c for c in clients if c["id"] == 1)["name"])

    def test_GetClient_ReturnsTheClient(self):
        client = self.get_json(f"{self.url}/1")

        self.assertEqual("Amersfoort", client["city"])
        self.assertEqual("9704WL", client["zip_code"])
        self.assertEqual(datetime(2024, 5, 2, 19, 57, 33, tzinfo=timezone.utc), parse_time(client["created_at"]))

    def test_GetRoutes_UnknownClient_ReturnNotFound(self):
        for route in ["", "/orders"]:
            with self.subTest(route=route):
                response = self.get(f"{self.url}/999999{route}")

                self.assertEqual(404, response.status)

    def test_GetClientOrders_ReturnsOrdersForAllThreeClientFieldsWithItems(self):
        order_ids = {
            o["id"] for o in self.rows("order")
            if 1 in (o["client_id"], o["ship_to_client_id"], o["bill_to_client_id"])}
        expected_items = sum(1 for i in self.rows("order_item") if i["order_id"] in order_ids)

        orders = self.get_json(f"{self.url}/1/orders")

        self.assertEqual(len(order_ids), len(orders))
        self.assertEqual(len(orders), len({o["id"] for o in orders}))
        for o in orders:
            self.assertIn(1, (o["client_id"], o["ship_to_client_id"], o["bill_to_client_id"]))
        self.assertEqual(expected_items, sum(len(o["items"]) for o in orders))

    def test_GetClientOrders_ClientWithoutOrders_ReturnsEmptyList(self):
        created = self.create(new_client())

        orders = self.get_json(f"{self.url}/{created['id']}/orders")

        self.assertEqual([], orders)

    def test_CreateClient_StoresTheClient(self):
        client = new_client()
        self.with_id(client)
        before = utc_now() - timedelta(seconds=1)

        response = self.post(self.url, client)

        created = self.created_body(response)
        self.assertEqual(f"{self.url}/{created['id']}", response.location_path())
        self.assertGreater(created["id"], 300)
        self.assertGreaterEqual(parse_time(created["created_at"]), before)
        self.assert_same_time(created["created_at"], created["updated_at"])

        stored = self.get_json(f"{self.url}/{created['id']}")
        self.assertEqual("Jumbo Test", stored["name"])
        self.assertEqual("test@example.com", stored["contact_email"])

    def test_CreateClient_IgnoresIdAndTimestampsFromTheRequest(self):
        client = new_client()
        client["id"] = 1
        client["created_at"] = "2000-01-01T00:00:00Z"

        created = self.created_body(self.post(self.url, client))

        self.assertNotEqual(1, created["id"])
        self.assertGreaterEqual(parse_time(created["created_at"]).year, 2026)

    def test_CreateClient_MissingField_ReturnsBadRequest(self):
        for field, value in [("name", ""), ("city", "  "), ("contact_email", "")]:
            with self.subTest(field=field, value=value):
                client = new_client()
                self.with_id(client)
                client[field] = value

                response = self.post(self.url, client)

                self.assertEqual(400, response.status)

    def test_UpdateClient_ChangesFieldsButKeepsCreatedAt(self):
        created = self.create(new_client())
        time.sleep(0.01)

        response = self.put(f"{self.url}/{created['id']}", new_client(name="Jumbo Changed"))

        self.assert_no_content(response)
        updated = self.get_json(f"{self.url}/{created['id']}")
        self.assertEqual("Jumbo Changed", updated["name"])
        self.assert_same_time(created["created_at"], updated.get("created_at"))
        self.assertGreater(parse_time(updated["updated_at"]), parse_time(created["updated_at"]))

    def test_UpdateClient_UnknownClient_ReturnsNotFound(self):
        response = self.put(f"{self.url}/999999", new_client())

        self.assertEqual(404, response.status)

    def test_UpdateClient_MissingField_ReturnsBadRequest(self):
        created = self.create(new_client())

        response = self.put(f"{self.url}/{created['id']}", new_client(name=""))

        self.assertEqual(400, response.status)

    def test_DeleteClient_RemovesTheClient(self):
        created = self.create(new_client())

        response = self.delete(f"{self.url}/{created['id']}")

        self.assert_no_content(response)
        self.assertEqual(404, self.get(f"{self.url}/{created['id']}").status)

    def test_DeleteClient_WithOrders_ReturnsConflictAndKeepsTheClient(self):
        response = self.delete(f"{self.url}/1")

        self.assertEqual(409, response.status)
        self.assertEqual(200, self.get(f"{self.url}/1").status)

    def test_DeleteClient_UnknownClient_ReturnsNotFound(self):
        response = self.delete(f"{self.url}/999999")

        self.assertEqual(404, response.status)
