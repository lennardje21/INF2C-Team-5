"""v1 counterpart of WarehousesControllerTests.cs."""

import uuid

from support import ApiTestCase, parse_time


def new_warehouse(name="Test Warehouse"):
    return {
        "code": f"TEST-{uuid.uuid4().hex}",
        "name": name,
        "address": "Test Street 1",
        "city": "Rotterdam",
        "zip_code": "3000AA",
        "province": "Zuid-Holland",
        "country": "Netherlands",
        "contact_name": "Test Person",
        "contact_phone": "0101234567",
        "contact_email": "test@example.com",
    }


class WarehousesControllerTests(ApiTestCase):
    url = "/api/v1/warehouses"
    file = "warehouse"

    def add_location(self, warehouse_id, name):
        """Adds a location directly to the data, as the C# test inserts into MySQL."""
        locations = self.records("location")
        locations.append({
            "id": self.next_id("location"),
            "warehouse_id": warehouse_id,
            "code": f"LOC-{uuid.uuid4().hex}",
            "name": name,
            "created_at": "2026-01-01T00:00:00Z",
            "updated_at": "2026-01-01T00:00:00Z",
        })
        self.write_records("location", locations)

    def remove_locations(self, warehouse_id):
        self.write_records(
            "location", [x for x in self.records("location") if x["warehouse_id"] != warehouse_id])

    def test_GetWarehouses_ReturnsSeededWarehouses(self):
        warehouses = self.get_json(self.url)

        self.assertIsNotNone(warehouses)
        self.assertTrue(warehouses)
        self.assertTrue(any(w["id"] == 1 for w in warehouses))

    def test_GetWarehouse_ReturnsTheWarehouse(self):
        warehouse = self.get_json(f"{self.url}/1")

        self.assertEqual(1, warehouse["id"])
        self.assertTrue(warehouse["name"])

    def test_GetRoutes_UnknownWarehouse_ReturnNotFound(self):
        for route in ["", "/locations"]:
            with self.subTest(route=route):
                response = self.get(f"{self.url}/2147483647{route}")

                self.assertEqual(404, response.status)

    def test_CreateWarehouse_StoresTheWarehouse(self):
        warehouse = new_warehouse()
        warehouse["id"] = self.next_id(self.file)

        response = self.post(self.url, warehouse)

        created = self.created_body(response)
        self.assertEqual(f"{self.url}/{created['id']}", response.location_path())
        self.assertGreater(created["id"], 0)
        self.assertTrue(created.get("created_at"))
        self.assertEqual("Test Warehouse", self.get_json(f"{self.url}/{created['id']}")["name"])

    def test_CreateWarehouse_IgnoresIdAndTimestampsFromRequest(self):
        warehouse = new_warehouse()
        warehouse["id"] = 1
        warehouse["created_at"] = "2000-01-01T00:00:00Z"
        warehouse["updated_at"] = "2000-01-01T00:00:00Z"

        created = self.created_body(self.post(self.url, warehouse))

        self.assertNotEqual(1, created["id"])
        self.assertGreaterEqual(parse_time(created["created_at"]).year, 2026)

    def test_CreateWarehouse_InvalidField_ReturnsBadRequest(self):
        for field, value in [("name", ""), ("city", "  "), ("contact_email", "not-an-email")]:
            with self.subTest(field=field, value=value):
                warehouse = new_warehouse()
                warehouse["id"] = self.next_id(self.file)
                warehouse[field] = value

                response = self.post(self.url, warehouse)

                self.assertEqual(400, response.status)

    def test_UpdateWarehouse_ChangesTheWarehouse(self):
        created = self.create(new_warehouse(), self.file)

        response = self.put(f"{self.url}/{created['id']}", new_warehouse(name="Changed Warehouse"))

        self.assert_no_content(response)
        updated = self.get_json(f"{self.url}/{created['id']}")
        self.assertEqual("Changed Warehouse", updated["name"])
        self.assertEqual(created["created_at"], updated.get("created_at"))

    def test_UpdateWarehouse_InvalidField_ReturnsBadRequest(self):
        created = self.create(new_warehouse(), self.file)
        warehouse = new_warehouse()
        warehouse["name"] = ""

        response = self.put(f"{self.url}/{created['id']}", warehouse)

        self.assertEqual(400, response.status)
        self.assertEqual(created["name"], self.get_json(f"{self.url}/{created['id']}")["name"])

    def test_GetWarehouseLocations_ReturnsLocationsForThatWarehouse(self):
        warehouse = self.create(new_warehouse(), self.file)
        self.add_location(warehouse["id"], "Test Location")

        locations = self.get_json(f"{self.url}/{warehouse['id']}/locations")

        self.assertEqual(1, len(locations))
        self.assertEqual(warehouse["id"], locations[0]["warehouse_id"])
        self.assertEqual("Test Location", locations[0]["name"])

    def test_DeleteWarehouse_RemovesTheWarehouse(self):
        warehouse = self.create(new_warehouse(), self.file)

        response = self.delete(f"{self.url}/{warehouse['id']}")

        self.assert_no_content(response)
        self.assertEqual(404, self.get(f"{self.url}/{warehouse['id']}").status)

    def test_DeleteWarehouse_WithLocations_ReturnsBadRequest(self):
        warehouse = self.create(new_warehouse(), self.file)
        self.add_location(warehouse["id"], "Referenced Location")

        response = self.delete(f"{self.url}/{warehouse['id']}")

        self.assertEqual(400, response.status)
        self.assertEqual(200, self.get(f"{self.url}/{warehouse['id']}").status)

        self.remove_locations(warehouse["id"])
        self.assert_no_content(self.delete(f"{self.url}/{warehouse['id']}"))

    def test_UpdateAndDeleteUnknownWarehouse_ReturnNotFound(self):
        update = self.put(f"{self.url}/2147483647", new_warehouse())
        delete = self.delete(f"{self.url}/2147483647")

        self.assertEqual(404, update.status)
        self.assertEqual(404, delete.status)
