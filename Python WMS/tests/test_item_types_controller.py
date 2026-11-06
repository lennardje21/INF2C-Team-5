"""v1 counterpart of ItemTypesControllerTests.cs."""

import time
import uuid
from datetime import timedelta

from support import ApiTestCase, parse_time, utc_now


def new_item_type(name=None):
    return {
        "name": name or f"Test Item Type {uuid.uuid4().hex}",
        "description": "Item Type for API tests",
    }


class ItemTypesControllerTests(ApiTestCase):
    url = "/api/v1/item_types"
    file = "item_type"

    def test_GetItemTypes_ReturnsSeededItemTypes(self):
        item_types = self.get_json(self.url)
        expected = len(self.records(self.file))

        self.assertIsNotNone(item_types)
        self.assertEqual(expected, len(item_types))
        self.assertTrue(item_types)

    def test_GetItemType_ReturnsTheItemType(self):
        created = self.create(new_item_type(), self.file)
        item_type = self.get_json(f"{self.url}/{created['id']}")

        self.assertIsNotNone(item_type)
        self.assertEqual(created["id"], item_type["id"])
        self.assertEqual(created["name"], item_type["name"])
        self.assertEqual(created["description"], item_type["description"])

    def test_GetItemType_UnknownItemType_ReturnsNotFound(self):
        response = self.get(f"{self.url}/999999")

        self.assertEqual(404, response.status)

    def test_CreateItemType_StoresTheItemType(self):
        item_type = new_item_type()
        item_type["id"] = self.next_id(self.file)
        before = utc_now() - timedelta(seconds=1)

        response = self.post(self.url, item_type)

        created = self.created_body(response)
        self.assertEqual(f"{self.url}/{created['id']}", response.location_path())
        self.assertGreater(created["id"], 0)
        self.assertGreaterEqual(parse_time(created["created_at"]), before)
        self.assertEqual(created["created_at"], created["updated_at"])

        stored = self.get_json(f"{self.url}/{created['id']}")
        self.assertEqual(item_type["name"], stored["name"])
        self.assertEqual(item_type["description"], stored["description"])

    def test_CreateItemType_IgnoresIdAndTimestampsFromTheRequest(self):
        item_type = new_item_type()
        item_type["id"] = 1
        item_type["created_at"] = "2000-01-01T00:00:00Z"
        item_type["updated_at"] = "2000-01-01T00:00:00Z"

        response = self.post(self.url, item_type)

        created = self.created_body(response)
        self.assertNotEqual(1, created["id"])
        self.assertNotEqual(2000, parse_time(created["created_at"]).year)
        self.assertNotEqual(2000, parse_time(created["updated_at"]).year)

    def test_CreateItemType_MissingField_ReturnsBadRequest(self):
        for field, value in [("name", ""), ("name", "  "), ("description", ""), ("description", "  ")]:
            with self.subTest(field=field, value=value):
                item_type = new_item_type()
                item_type["id"] = self.next_id(self.file)
                item_type[field] = value

                response = self.post(self.url, item_type)

                self.assertEqual(400, response.status)

    def test_CreateItemType_NameOver255Characters_ReturnsBadRequest(self):
        item_type = new_item_type()
        item_type["id"] = self.next_id(self.file)
        item_type["name"] = "x" * 256

        response = self.post(self.url, item_type)

        self.assertEqual(400, response.status)

    def test_UpdateItemType_ChangesFieldsButKeepsCreatedAt(self):
        created = self.create(new_item_type(), self.file)
        time.sleep(0.01)
        item_type = new_item_type(name="Updated Test Item Type")
        item_type["description"] = "Updated description"

        response = self.put(f"{self.url}/{created['id']}", item_type)

        self.assert_no_content(response)
        updated = self.get_json(f"{self.url}/{created['id']}")
        self.assertEqual("Updated Test Item Type", updated["name"])
        self.assertEqual("Updated description", updated["description"])
        self.assertEqual(created["created_at"], updated.get("created_at"))
        self.assertGreater(parse_time(updated["updated_at"]), parse_time(created["updated_at"]))

    def test_UpdateItemType_UnknownItemType_ReturnsNotFound(self):
        response = self.put(f"{self.url}/999999", new_item_type())

        self.assertEqual(404, response.status)

    def test_UpdateItemType_MissingField_ReturnsBadRequest(self):
        created = self.create(new_item_type(), self.file)
        item_type = new_item_type()
        item_type["name"] = ""

        response = self.put(f"{self.url}/{created['id']}", item_type)

        self.assertEqual(400, response.status)

    def test_DeleteItemType_RemovesTheItemType(self):
        created = self.create(new_item_type(), self.file)

        response = self.delete(f"{self.url}/{created['id']}")

        self.assert_no_content(response)
        self.assertEqual(404, self.get(f"{self.url}/{created['id']}").status)

    def test_DeleteItemType_WithItems_ReturnsBadRequestAndKeepsTheItemType(self):
        id = self.records("item")[0]["item_type_id"]

        response = self.delete(f"{self.url}/{id}")

        self.assertEqual(400, response.status)
        self.assertEqual(200, self.get(f"{self.url}/{id}").status)

    def test_DeleteItemType_UnknownItemType_ReturnsNotFound(self):
        response = self.delete(f"{self.url}/999999")

        self.assertEqual(404, response.status)
