"""v1 counterpart of OrdersControllerTests.cs."""

import random
import time
from datetime import datetime, timedelta, timezone

from support import ApiTestCase, parse_time, raw_numbers, run_concurrently, utc_now


def new_order(client_id=1, warehouse_id=1, order_status="Pending",
              request_date="2026-10-30T09:00:00Z", items=()):
    return {
        "client_id": client_id,
        "order_date": "2026-10-26T09:00:00Z",
        "request_date": request_date,
        "customer_po_number": "PO-TEST",
        "order_status": order_status,
        "shipping_notes": None,
        "warehouse_id": warehouse_id,
        "ship_to_client_id": 2,
        "bill_to_client_id": 3,
        "items": list(items),
    }


def item(item_id, amount, unit_price=2.50):
    return {"item_id": item_id, "amount": amount, "unit_price": unit_price}


def reference_for(id):
    return f"ORD-{id:06d}"


def invalid_orders():
    missing_date = new_order()
    del missing_date["order_date"]
    missing_po = new_order()
    missing_po["customer_po_number"] = " "
    unknown_ship_to = new_order()
    unknown_ship_to["ship_to_client_id"] = 999999
    return [
        ("unknown client", new_order(client_id=999999)),
        ("unknown warehouse", new_order(warehouse_id=999999)),
        ("unknown ship-to client", unknown_ship_to),
        ("empty status", new_order(order_status="")),
        ("blank PO number", missing_po),
        ("missing order date", missing_date),
        ("unknown item", new_order(items=[item(999999, 1)])),
        ("zero amount", new_order(items=[item(50, 0)])),
        ("negative price", new_order(items=[item(50, 1, -1)])),
        ("duplicate item", new_order(items=[item(50, 1), item(50, 2)])),
    ]


class OrdersControllerTests(ApiTestCase):
    url = "/api/v1/orders"
    file = "order"

    def quantity_allocated(self, item_id):
        return self.inventory_field(item_id, "quantity_allocated")

    def order_item_count(self, order_id):
        return sum(1 for i in self.records("order_item") if i["order_id"] == order_id)

    def raw_prices(self, url):
        response = self.get(url)
        self.assertEqual(200, response.status)
        return [i["unit_price"] for i in raw_numbers(response.body)]

    def test_GetOrders_ReturnsSeededOrdersWithItems(self):
        orders = self.get_json(self.url)

        self.assertGreaterEqual(len(orders), 4854)
        first = next(o for o in orders if o["id"] == 1)
        self.assertEqual("ORD-000001", first["reference"])
        self.assertTrue(first["items"])

    def test_GetOrder_ReturnsOrderWithItems(self):
        order = self.get_json(f"{self.url}/1")

        self.assertEqual(123, order["client_id"])
        self.assertEqual(2, order["warehouse_id"])
        self.assertEqual((31, 87), (order["ship_to_client_id"], order["bill_to_client_id"]))
        self.assertEqual(datetime(2025, 3, 5, 4, 42, 29, tzinfo=timezone.utc), parse_time(order["order_date"]))
        self.assertEqual(self.order_item_count(1), len(order["items"]))

    def test_GetRoutes_UnknownOrder_ReturnNotFound(self):
        for route in ["", "/items"]:
            with self.subTest(route=route):
                response = self.get(f"{self.url}/999999{route}")

                self.assertEqual(404, response.status)

    def test_GetOrderItems_ReturnsPricesWithTwoDecimals(self):
        prices = self.raw_prices(f"{self.url}/1/items")

        self.assertEqual("21.34", prices[0])
        for p in prices:
            self.assertRegex(p, r"^\d+\.\d{2}$")

    def test_CreateOrder_StoresOrderAndItems(self):
        order = new_order(items=[item(50, 2, 9.99)])
        order["id"] = self.next_id(self.file)
        before = utc_now() - timedelta(seconds=1)

        response = self.post(self.url, order)

        created = self.created_body(response)
        self.assertEqual(f"{self.url}/{created['id']}", response.location_path())
        self.assertGreater(created["id"], 4854)
        self.assertEqual(reference_for(created["id"]), created.get("reference"))
        self.assertGreaterEqual(parse_time(created["created_at"]), before)
        self.assertEqual(created["created_at"], created["updated_at"])

        stored = self.get_json(f"{self.url}/{created['id']}")
        self.assertEqual(reference_for(created["id"]), stored.get("reference"))
        self.assertIsNone(stored["shipping_notes"])
        self.assertEqual(1, len(stored["items"]))
        stored_item = stored["items"][0]
        self.assertEqual((50, 2, 9.99), (stored_item["item_id"], stored_item["amount"], stored_item.get("unit_price")))

    def test_CreateOrder_RoundsPricesToTwoDecimals(self):
        created = self.create(new_order(items=[item(50, 1, 9.9), item(51, 1, 1.005), item(52, 1, 5)]), self.file)

        prices = self.raw_prices(f"{self.url}/{created['id']}/items")

        self.assertEqual(["9.90", "1.01", "5.00"], prices)

    def test_CreateOrder_IgnoresIdReferenceAndTimestampsFromTheRequest(self):
        order = new_order()
        order["id"] = 1
        order["reference"] = "ORD-000001"
        order["created_at"] = "2000-01-01T00:00:00Z"

        created = self.created_body(self.post(self.url, order))

        self.assertNotEqual(1, created["id"])
        self.assertEqual(reference_for(created["id"]), created["reference"])
        self.assertGreaterEqual(parse_time(created["created_at"]).year, 2026)

    def test_CreateOrder_DateWithoutTimeZone_IsStoredAsUtc(self):
        created = self.create(new_order(request_date="2026-10-30T09:00:00"), self.file)

        stored = self.get_json(f"{self.url}/{created['id']}")
        self.assertEqual("2026-10-30T09:00:00Z", stored["request_date"])

    def test_CreateOrder_ConcurrentRequests_EachGetTheirOwnReference(self):
        # A v1 client picks the ids itself, so each request gets its own.
        first_id = self.next_id(self.file)

        def create(n):
            order = new_order()
            order["id"] = first_id + n
            self.assertEqual(201, self.post(self.url, order).status)
            return self.get_json(f"{self.url}/{order['id']}")

        created = run_concurrently(create, range(20))

        self.assertEqual(20, len({o["id"] for o in created}))
        for o in created:
            self.assertEqual(reference_for(o["id"]), o.get("reference"))

    def test_CreateOrder_InvalidInput_ReturnsBadRequest(self):
        for name, order in invalid_orders():
            with self.subTest(name):
                order["id"] = self.next_id(self.file)
                count = len(self.records(self.file))

                response = self.post(self.url, order)

                self.assertEqual(400, response.status)
                self.assertEqual(count, len(self.records(self.file)))

    def test_UpdateOrder_ChangesFieldsButKeepsReferenceCreatedAtAndItems(self):
        created = self.create(new_order(items=[item(53, 4)]), self.file)
        time.sleep(0.01)
        changes = new_order(order_status="Shipped")
        changes["reference"] = "ORD-CHANGED"
        changes["shipping_notes"] = "Achterom leveren"

        response = self.put(f"{self.url}/{created['id']}", changes)

        self.assert_no_content(response)
        updated = self.get_json(f"{self.url}/{created['id']}")
        self.assertEqual("Shipped", updated["order_status"])
        self.assertEqual("Achterom leveren", updated["shipping_notes"])
        self.assertEqual(reference_for(created["id"]), updated["reference"])
        self.assertEqual(created["created_at"], updated.get("created_at"))
        self.assertGreater(parse_time(updated["updated_at"]), parse_time(created["updated_at"]))
        self.assertEqual([53], [i["item_id"] for i in updated["items"]])

    def test_UpdateOrder_UnknownOrder_ReturnsNotFound(self):
        response = self.put(f"{self.url}/999999", new_order())

        self.assertEqual(404, response.status)

    def test_UpdateOrder_UnknownWarehouse_ReturnsBadRequest(self):
        created = self.create(new_order(), self.file)

        response = self.put(f"{self.url}/{created['id']}", new_order(warehouse_id=999999))

        self.assertEqual(400, response.status)

    def test_UpdateOrderItems_ReplacesItemsAndUpdatesInventory(self):
        created = self.create(new_order(items=[item(60, 6), item(61, 3)]), self.file)
        q60, q61, q62 = (self.quantity_allocated(60), self.quantity_allocated(61), self.quantity_allocated(62))

        # 60 goes up by 4, 61 is removed and 62 is added.
        response = self.put(f"{self.url}/{created['id']}/items", [item(60, 10), item(62, 5)])

        self.assert_no_content(response)
        items = self.get_json(f"{self.url}/{created['id']}/items")
        self.assertEqual([(60, 10), (62, 5)], sorted((i["item_id"], i["amount"]) for i in items))
        self.assertEqual(q60 + 4, self.quantity_allocated(60))
        self.assertEqual(max(0, q61 - 3), self.quantity_allocated(61))
        self.assertEqual(q62 + 5, self.quantity_allocated(62))

    def test_UpdateOrderItems_InvalidItems_ChangeNothing(self):
        created = self.create(new_order(items=[item(63, 2)]), self.file)
        q63 = self.quantity_allocated(63)

        # The first item is valid, so this also checks that the change is rolled back.
        response = self.put(f"{self.url}/{created['id']}/items", [item(63, 8), item(999999, 1)])

        self.assertEqual(400, response.status)
        items = self.get_json(f"{self.url}/{created['id']}/items")
        self.assertEqual([(63, 2)], [(i["item_id"], i["amount"]) for i in items])
        self.assertEqual(q63, self.quantity_allocated(63))

    def test_UpdateOrderItems_UnknownOrder_ReturnsNotFound(self):
        response = self.put(f"{self.url}/999999/items", [item(60, 1)])

        self.assertEqual(404, response.status)

    def test_UpdateOrderItems_ConcurrentRequests_KeepInventoryConsistent(self):
        # Starting at 1 means no request can push quantity_allocated below 0.
        created = self.create(new_order(items=[item(70, 1)]), self.file)
        before = self.quantity_allocated(70)

        rng = random.Random(42)
        amounts = [rng.randint(1, 40) for _ in range(50)]
        run_concurrently(
            lambda amount: self.put(f"{self.url}/{created['id']}/items", [item(70, amount)]), amounts)

        items = self.get_json(f"{self.url}/{created['id']}/items")
        self.assertEqual(1, len(items))
        self.assertEqual(before + items[0]["amount"] - 1, self.quantity_allocated(70))

    def test_DeleteOrder_RemovesOrderAndItems(self):
        created = self.create(new_order(items=[item(54, 1)]), self.file)

        response = self.delete(f"{self.url}/{created['id']}")

        self.assert_no_content(response)
        self.assertEqual(404, self.get(f"{self.url}/{created['id']}").status)
        self.assertEqual(0, self.order_item_count(created["id"]))

    def test_DeleteOrder_WithShipments_ReturnsConflictAndKeepsTheOrder(self):
        items = self.order_item_count(1)

        response = self.delete(f"{self.url}/1")

        self.assertEqual(409, response.status)
        self.assertEqual(200, self.get(f"{self.url}/1").status)
        self.assertEqual(items, self.order_item_count(1))

    def test_DeleteOrder_UnknownOrder_ReturnsNotFound(self):
        response = self.delete(f"{self.url}/999999")

        self.assertEqual(404, response.status)
