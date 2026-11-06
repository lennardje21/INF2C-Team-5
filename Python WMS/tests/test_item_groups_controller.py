"""v1 counterpart of ItemGroupsControllerTests.cs."""

import time
import uuid
from datetime import timedelta

from support import ApiTestCase, parse_time, utc_now


def new_item_group(name=None):
    return {
        "name": name or f"Test Item Group {uuid.uuid4().hex}",
        "description": "Item group for API tests",
    }


class ItemGroupsControllerTests(ApiTestCase):
    url = "/api/v1/item_groups"
    file = "item_group"

    def test_GetItemGroups_ReturnsSeededItemGroups(self):
        item_groups = self.get_json(self.url)
        expected = len(self.records(self.file))

        self.assertIsNotNone(item_groups)
        self.assertEqual(expected, len(item_groups))
        self.assertTrue(item_groups)

    def test_GetItemGroup_ReturnsTheItemGroup(self):
        created = self.create(new_item_group(), self.file)
        item_group = self.get_json(f"{self.url}/{created['id']}")

        self.assertIsNotNone(item_group)
        self.assertEqual(created["id"], item_group["id"])
        self.assertEqual(created["name"], item_group["name"])
        self.assertEqual(created["description"], item_group["description"])

    def test_GetItemGroup_UnknownItemGroup_ReturnsNotFound(self):
        response = self.get(f"{self.url}/999999")

        self.assertEqual(404, response.status)

    def test_CreateItemGroup_StoresTheItemGroup(self):
        item_group = new_item_group()
        item_group["id"] = self.next_id(self.file)
        before = utc_now() - timedelta(seconds=1)

        response = self.post(self.url, item_group)

        created = self.created_body(response)
        self.assertEqual(f"{self.url}/{created['id']}", response.location_path())
        self.assertGreater(created["id"], 0)
        self.assertGreaterEqual(parse_time(created["created_at"]), before)
        self.assertEqual(created["created_at"], created["updated_at"])

        stored = self.get_json(f"{self.url}/{created['id']}")
        self.assertEqual(item_group["name"], stored["name"])
        self.assertEqual(item_group["description"], stored["description"])

    def test_CreateItemGroup_IgnoresIdAndTimestampsFromTheRequest(self):
        item_group = new_item_group()
        item_group["id"] = 1
        item_group["created_at"] = "2000-01-01T00:00:00Z"
        item_group["updated_at"] = "2000-01-01T00:00:00Z"

        response = self.post(self.url, item_group)

        created = self.created_body(response)
        self.assertNotEqual(1, created["id"])
        self.assertNotEqual(2000, parse_time(created["created_at"]).year)
        self.assertNotEqual(2000, parse_time(created["updated_at"]).year)

    def test_CreateItemGroup_MissingField_ReturnsBadRequest(self):
        for field, value in [("name", ""), ("name", "  "), ("description", ""), ("description", "  ")]:
            with self.subTest(field=field, value=value):
                item_group = new_item_group()
                item_group["id"] = self.next_id(self.file)
                item_group[field] = value

                response = self.post(self.url, item_group)

                self.assertEqual(400, response.status)

    def test_CreateItemGroup_NameOver255Characters_ReturnsBadRequest(self):
        item_group = new_item_group()
        item_group["id"] = self.next_id(self.file)
        item_group["name"] = "x" * 256

        response = self.post(self.url, item_group)

        self.assertEqual(400, response.status)

    def test_UpdateItemGroup_ChangesFieldsButKeepsCreatedAt(self):
        created = self.create(new_item_group(), self.file)
        time.sleep(0.01)
        item_group = new_item_group(name="Updated Test Item Group")
        item_group["description"] = "Updated description"

        response = self.put(f"{self.url}/{created['id']}", item_group)

        self.assert_no_content(response)
        updated = self.get_json(f"{self.url}/{created['id']}")
        self.assertEqual("Updated Test Item Group", updated["name"])
        self.assertEqual("Updated description", updated["description"])
        self.assertEqual(created["created_at"], updated.get("created_at"))
        self.assertGreater(parse_time(updated["updated_at"]), parse_time(created["updated_at"]))

    def test_UpdateItemGroup_UnknownItemGroup_ReturnsNotFound(self):
        response = self.put(f"{self.url}/999999", new_item_group())

        self.assertEqual(404, response.status)

    def test_UpdateItemGroup_MissingField_ReturnsBadRequest(self):
        created = self.create(new_item_group(), self.file)
        item_group = new_item_group()
        item_group["name"] = ""

        response = self.put(f"{self.url}/{created['id']}", item_group)

        self.assertEqual(400, response.status)

    def test_DeleteItemGroup_RemovesTheItemGroup(self):
        created = self.create(new_item_group(), self.file)

        response = self.delete(f"{self.url}/{created['id']}")

        self.assert_no_content(response)
        self.assertEqual(404, self.get(f"{self.url}/{created['id']}").status)

    def test_DeleteItemGroup_WithItems_ReturnsBadRequestAndKeepsTheItemGroup(self):
        id = self.records("item")[0]["item_group_id"]

        response = self.delete(f"{self.url}/{id}")

        self.assertEqual(400, response.status)
        self.assertEqual(200, self.get(f"{self.url}/{id}").status)

    def test_DeleteItemGroup_UnknownItemGroup_ReturnsNotFound(self):
        response = self.delete(f"{self.url}/999999")

        self.assertEqual(404, response.status)
