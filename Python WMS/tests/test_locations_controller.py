"""Python version of LocationsControllerTests.cs; runs against v1 or v2."""

import uuid

from support import ApiTestCase, parse_time


def new_location(name="Test Location"):
    return {
        "warehouse_id": 1,
        "code": f"TEST-{uuid.uuid4().hex}",
        "name": name,
    }


class LocationsControllerTests(ApiTestCase):
    url = "/api/v1/locations"
    file = "location"

    def test_GetLocations_ReturnsSeededLocations(self):
        locations = self.get_json(self.url)

        self.assertIsNotNone(locations)
        self.assertTrue(locations)
        self.assertTrue(any(x["warehouse_id"] > 0 for x in locations))

    def test_GetLocation_ReturnsTheLocation(self):
        location = self.get_json(f"{self.url}/1")

        self.assertEqual(1, location["id"])
        self.assertEqual(1, location["warehouse_id"])
        self.assertTrue(location["code"])
        self.assertTrue(location["name"])

    def test_GetLocation_UnknownLocation_ReturnsNotFound(self):
        response = self.get(f"{self.url}/2147483647")

        self.assertEqual(404, response.status)

    def test_CreateLocation_EmptyField_ReturnsBadRequest(self):
        for field, value in [("code", ""), ("name", " ")]:
            with self.subTest(field=field, value=value):
                location = new_location()
                self.with_id(location)
                location[field] = value

                response = self.post(self.url, location)

                self.assertEqual(400, response.status)

    def test_CreateLocation_InvalidWarehouseId_ReturnsBadRequest(self):
        for warehouse_id in [0, -1]:
            with self.subTest(warehouse_id=warehouse_id):
                location = new_location()
                self.with_id(location)
                location["warehouse_id"] = warehouse_id

                response = self.post(self.url, location)

                self.assertEqual(400, response.status)

    def test_CreateLocation_FieldOver255Characters_ReturnsBadRequest(self):
        for field in ["code", "name"]:
            with self.subTest(field=field):
                location = new_location()
                self.with_id(location)
                location[field] = "x" * 256

                response = self.post(self.url, location)

                self.assertEqual(400, response.status)

    def test_CreateLocation_IgnoresIdAndTimestampsFromRequest(self):
        location = new_location()
        location["id"] = 1
        location["created_at"] = "2000-01-01T00:00:00Z"
        location["updated_at"] = "2000-01-01T00:00:00Z"

        created = self.created_body(self.post(self.url, location))

        self.assertNotEqual(1, created["id"])
        self.assertEqual(1, created["warehouse_id"])
        self.assertGreaterEqual(parse_time(created["created_at"]).year, 2026)
        self.assert_same_time(created["created_at"], created["updated_at"])

    def test_UpdateLocation_ChangesTheLocation(self):
        created = self.create(new_location())

        response = self.put(f"{self.url}/{created['id']}", new_location(name="Updated Location"))

        self.assert_no_content(response)
        updated = self.get_json(f"{self.url}/{created['id']}")
        self.assertEqual("Updated Location", updated["name"])
        self.assert_same_time(created["created_at"], updated.get("created_at"))

    def test_UpdateLocation_InvalidField_ReturnsBadRequest(self):
        created = self.create(new_location())
        location = new_location()
        location["name"] = ""

        response = self.put(f"{self.url}/{created['id']}", location)

        self.assertEqual(400, response.status)
        self.assertEqual(created["name"], self.get_json(f"{self.url}/{created['id']}")["name"])

    def test_UpdateLocation_UnknownLocation_ReturnsNotFound(self):
        response = self.put(f"{self.url}/2147483647", new_location())

        self.assertEqual(404, response.status)

    def test_DeleteLocation_RemovesTheLocation(self):
        created = self.create(new_location())

        response = self.delete(f"{self.url}/{created['id']}")

        self.assert_no_content(response)
        self.assertEqual(404, self.get(f"{self.url}/{created['id']}").status)

    def test_DeleteLocation_WithInventory_ReturnsBadRequest(self):
        response = self.delete(f"{self.url}/1")

        self.assertEqual(400, response.status)
        self.assertEqual(200, self.get(f"{self.url}/1").status)

    def test_DeleteLocation_UnknownLocation_ReturnsNotFound(self):
        response = self.delete(f"{self.url}/2147483647")

        self.assertEqual(404, response.status)
