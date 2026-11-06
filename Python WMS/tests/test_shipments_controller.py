"""Python version of ShipmentsControllerTests.cs; runs against v1 or v2."""

import random
import time
from datetime import datetime, timedelta, timezone

from support import ApiTestCase, parse_time, run_concurrently, utc_now


def new_shipment(order_id=3, shipment_type="Outgoing", shipment_status="Scheduled",
                 shipment_date="2026-10-12T10:00:00Z", items=()):
    return {
        "order_id": order_id,
        "shipment_date": shipment_date,
        "shipment_type": shipment_type,
        "shipment_status": shipment_status,
        "carrier_name": "DHL Parcel",
        "shipping_method": "Express",
        "payment_type": "Manual",
        "items": list(items),
    }


def item(item_id, amount):
    return {"item_id": item_id, "amount": amount}


def reference_for(id):
    return f"SHP-{id:06d}"


def invalid_shipments():
    return [
        ("unknown order", new_shipment(order_id=999999)),
        ("empty type", new_shipment(shipment_type="")),
        ("blank status", new_shipment(shipment_status="  ")),
        ("unknown item", new_shipment(items=[item(999999, 1)])),
        ("zero amount", new_shipment(items=[item(10, 0)])),
        ("duplicate item", new_shipment(items=[item(10, 1), item(10, 2)])),
    ]


class ShipmentsControllerTests(ApiTestCase):
    url = "/api/v1/shipments"
    file = "shipment"

    def quantity_ordered(self, item_id):
        return self.inventory_field(item_id, "quantity_ordered")

    def shipment_item_count(self, shipment_id):
        return self.count("shipment_item", shipment_id=shipment_id)

    def test_GetShipments_ReturnsSeededShipmentsWithItems(self):
        shipments = self.get_json(self.url)

        self.assertGreaterEqual(len(shipments), 6132)
        first = next(s for s in shipments if s["id"] == 1)
        self.assertEqual("SHP-000001", first["reference"])
        self.assertEqual([82], [i["item_id"] for i in first["items"]])

    def test_GetShipment_ReturnsShipmentWithItems(self):
        shipment = self.get_json(f"{self.url}/1")

        self.assertEqual(1, shipment["order_id"])
        self.assertEqual(datetime(2025, 3, 5, 18, 51, 53, tzinfo=timezone.utc), parse_time(shipment["shipment_date"]))
        self.assertEqual([(82, 6)], [(i["item_id"], i["amount"]) for i in shipment["items"]])

    def test_GetRoutes_UnknownShipment_ReturnNotFound(self):
        for route in ["", "/orders", "/items"]:
            with self.subTest(route=route):
                response = self.get(f"{self.url}/999999{route}")

                self.assertEqual(404, response.status)

    def test_GetShipmentOrders_ReturnsTheOrderId(self):
        self.assertEqual([1], self.get_json(f"{self.url}/1/orders"))

    def test_GetShipmentItems_ReturnsTheItems(self):
        items = self.get_json(f"{self.url}/2/items")

        self.assertEqual(self.shipment_item_count(2), len(items))
        self.assertGreater(len(items), 1)
        for i in items:
            self.assertEqual(2, i.get("shipment_id"))

    def test_CreateShipment_StoresShipmentAndItems(self):
        shipment = new_shipment(items=[item(10, 2)])
        self.with_id(shipment)
        before = utc_now() - timedelta(seconds=1)

        response = self.post(self.url, shipment)

        created = self.created_body(response)
        self.assertEqual(f"{self.url}/{created['id']}", response.location_path())
        self.assertGreater(created["id"], 6132)
        self.assertEqual(reference_for(created["id"]), created.get("reference"))
        self.assertGreaterEqual(parse_time(created["created_at"]), before)
        self.assert_same_time(created["created_at"], created["updated_at"])

        stored = self.get_json(f"{self.url}/{created['id']}")
        self.assertEqual(reference_for(created["id"]), stored.get("reference"))
        self.assertEqual([(10, 2)], [(i["item_id"], i["amount"]) for i in stored["items"]])

    def test_CreateShipment_IgnoresIdReferenceAndTimestampsFromTheRequest(self):
        shipment = new_shipment()
        shipment["id"] = 1
        shipment["reference"] = "SHP-000001"
        shipment["created_at"] = "2000-01-01T00:00:00Z"

        created = self.created_body(self.post(self.url, shipment))

        self.assertNotEqual(1, created["id"])
        self.assertEqual(reference_for(created["id"]), created["reference"])
        self.assertGreaterEqual(parse_time(created["created_at"]).year, 2026)

    def test_CreateShipment_ConcurrentRequests_EachGetTheirOwnReference(self):
        # A v1 client picks the ids itself, so each request gets its own.
        first_id = self.api.next_id(self.file) if self.api.client_sets_ids else None

        def create(n):
            shipment = new_shipment()
            if first_id:
                shipment["id"] = first_id + n
            response = self.post(self.url, shipment)
            self.assertEqual(201, response.status)
            return self.get_json(f"{self.url}/{self.created_id(shipment, response)}")

        created = run_concurrently(create, range(20))

        self.assertEqual(20, len({s["id"] for s in created}))
        for s in created:
            self.assertEqual(reference_for(s["id"]), s.get("reference"))

    def test_CreateShipment_DateWithoutTimeZone_IsStoredAsUtc(self):
        created = self.create(new_shipment(shipment_date="2026-10-12T10:00:00"))

        stored = self.get_json(f"{self.url}/{created['id']}")
        self.assertEqual("2026-10-12T10:00:00Z", stored["shipment_date"])

    def test_CreateShipment_InvalidInput_ReturnsBadRequest(self):
        for name, shipment in invalid_shipments():
            with self.subTest(name):
                self.with_id(shipment)
                count = self.count(self.file)

                response = self.post(self.url, shipment)

                self.assertEqual(400, response.status)
                self.assertEqual(count, self.count(self.file))

    def test_UpdateShipment_ChangesFieldsButKeepsReferenceCreatedAtAndItems(self):
        created = self.create(new_shipment(items=[item(11, 4)]))
        time.sleep(0.01)
        changes = new_shipment(shipment_status="Transit")
        changes["reference"] = "SHP-CHANGED"

        response = self.put(f"{self.url}/{created['id']}", changes)

        self.assert_no_content(response)
        updated = self.get_json(f"{self.url}/{created['id']}")
        self.assertEqual("Transit", updated["shipment_status"])
        self.assertEqual(reference_for(created["id"]), updated["reference"])
        self.assert_same_time(created["created_at"], updated.get("created_at"))
        self.assertGreater(parse_time(updated["updated_at"]), parse_time(created["updated_at"]))
        self.assertEqual([11], [i["item_id"] for i in updated["items"]])

    def test_UpdateShipment_UnknownShipment_ReturnsNotFound(self):
        response = self.put(f"{self.url}/999999", new_shipment())

        self.assertEqual(404, response.status)

    def test_UpdateShipment_UnknownOrder_ReturnsBadRequest(self):
        created = self.create(new_shipment())

        response = self.put(f"{self.url}/{created['id']}", new_shipment(order_id=999999))

        self.assertEqual(400, response.status)

    def test_UpdateShipmentOrders_ChangesTheOrder(self):
        created = self.create(new_shipment(order_id=3))

        response = self.put(f"{self.url}/{created['id']}/orders", [4])

        self.assert_no_content(response)
        self.assertEqual([4], self.get_json(f"{self.url}/{created['id']}/orders"))

    def test_UpdateShipmentOrders_InvalidOrders_ReturnsBadRequest(self):
        for order_ids in [[], [4, 5], [999999]]:
            with self.subTest(order_ids=order_ids):
                created = self.create(new_shipment())

                response = self.put(f"{self.url}/{created['id']}/orders", order_ids)

                self.assertEqual(400, response.status)

    def test_UpdateShipmentOrders_UnknownShipment_ReturnsNotFound(self):
        response = self.put(f"{self.url}/999999/orders", [4])

        self.assertEqual(404, response.status)

    def test_UpdateShipmentItems_ReplacesItemsAndUpdatesInventory(self):
        created = self.create(new_shipment(items=[item(20, 6), item(21, 3)]))
        q20, q21, q22 = (self.quantity_ordered(20), self.quantity_ordered(21), self.quantity_ordered(22))

        # 20 goes up by 4, 21 is removed and 22 is added.
        response = self.put(f"{self.url}/{created['id']}/items", [item(20, 10), item(22, 5)])

        self.assert_no_content(response)
        items = self.get_json(f"{self.url}/{created['id']}/items")
        self.assertEqual([(20, 10), (22, 5)], sorted((i["item_id"], i["amount"]) for i in items))
        self.assertEqual(q20 + 4, self.quantity_ordered(20))
        self.assertEqual(max(0, q21 - 3), self.quantity_ordered(21))
        self.assertEqual(q22 + 5, self.quantity_ordered(22))

    def test_UpdateShipmentItems_InvalidItems_ChangeNothing(self):
        created = self.create(new_shipment(items=[item(23, 2)]))
        q23 = self.quantity_ordered(23)

        # The first item is valid, so this also checks that the change is rolled back.
        response = self.put(f"{self.url}/{created['id']}/items", [item(23, 8), item(999999, 1)])

        self.assertEqual(400, response.status)
        items = self.get_json(f"{self.url}/{created['id']}/items")
        self.assertEqual([(23, 2)], [(i["item_id"], i["amount"]) for i in items])
        self.assertEqual(q23, self.quantity_ordered(23))

    def test_UpdateShipmentItems_UnknownShipment_ReturnsNotFound(self):
        response = self.put(f"{self.url}/999999/items", [item(20, 1)])

        self.assertEqual(404, response.status)

    def test_UpdateShipmentItems_ConcurrentRequests_KeepInventoryConsistent(self):
        # Starting at 1 means no request can push quantity_ordered below 0.
        created = self.create(new_shipment(items=[item(30, 1)]))
        before = self.quantity_ordered(30)

        rng = random.Random(42)
        amounts = [rng.randint(1, 40) for _ in range(50)]
        run_concurrently(
            lambda amount: self.put(f"{self.url}/{created['id']}/items", [item(30, amount)]), amounts)

        items = self.get_json(f"{self.url}/{created['id']}/items")
        self.assertEqual(1, len(items))
        self.assertEqual(before + items[0]["amount"] - 1, self.quantity_ordered(30))

    def test_DeleteShipment_RemovesShipmentAndItems(self):
        created = self.create(new_shipment(items=[item(12, 1)]))

        response = self.delete(f"{self.url}/{created['id']}")

        self.assert_no_content(response)
        self.assertEqual(404, self.get(f"{self.url}/{created['id']}").status)
        self.assertEqual(0, self.shipment_item_count(created["id"]))

    def test_DeleteShipment_UnknownShipment_ReturnsNotFound(self):
        response = self.delete(f"{self.url}/999999")

        self.assertEqual(404, response.status)
