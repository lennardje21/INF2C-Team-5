"""Python version of ItemLinesControllerTests.cs; runs against v1 or v2."""

import time
import uuid
from datetime import timedelta

from support import ApiTestCase, parse_time, utc_now


def new_item_line(name=None):
    return {
        "name": name or f"Test Item Line {uuid.uuid4().hex}",
        "description": "Item line for API tests",
    }


class ItemLinesControllerTests(ApiTestCase):
    url = "/api/v1/item_lines"
    file = "item_line"

    def test_GetItemLines_ReturnsSeededItemLines(self):
        item_lines = self.get_json(self.url)
        expected = self.count(self.file)

        self.assertIsNotNone(item_lines)
        self.assertEqual(expected, len(item_lines))
        self.assertTrue(item_lines)

    def test_GetItemLine_ReturnsTheItemLine(self):
        created = self.create(new_item_line())
        item_line = self.get_json(f"{self.url}/{created['id']}")

        self.assertIsNotNone(item_line)
        self.assertEqual(created["id"], item_line["id"])
        self.assertEqual(created["name"], item_line["name"])
        self.assertEqual(created["description"], item_line["description"])

    def test_GetItemLine_UnknownItemLine_ReturnsNotFound(self):
        response = self.get(f"{self.url}/999999")

        self.assertEqual(404, response.status)

    def test_CreateItemLine_StoresTheItemLine(self):
        item_line = new_item_line()
        self.with_id(item_line)
        before = utc_now() - timedelta(seconds=1)

        response = self.post(self.url, item_line)

        created = self.created_body(response)
        self.assertEqual(f"{self.url}/{created['id']}", response.location_path())
        self.assertGreater(created["id"], 0)
        self.assertGreaterEqual(parse_time(created["created_at"]), before)
        self.assert_same_time(created["created_at"], created["updated_at"])

        stored = self.get_json(f"{self.url}/{created['id']}")
        self.assertEqual(item_line["name"], stored["name"])
        self.assertEqual(item_line["description"], stored["description"])

    def test_CreateItemLine_IgnoresIdAndTimestampsFromTheRequest(self):
        item_line = new_item_line()
        item_line["id"] = 1
        item_line["created_at"] = "2000-01-01T00:00:00Z"
        item_line["updated_at"] = "2000-01-01T00:00:00Z"

        response = self.post(self.url, item_line)

        created = self.created_body(response)
        self.assertNotEqual(1, created["id"])
        self.assertNotEqual(2000, parse_time(created["created_at"]).year)
        self.assertNotEqual(2000, parse_time(created["updated_at"]).year)

    def test_CreateItemLine_MissingField_ReturnsBadRequest(self):
        for field, value in [("name", ""), ("name", "  "), ("description", ""), ("description", "  ")]:
            with self.subTest(field=field, value=value):
                item_line = new_item_line()
                self.with_id(item_line)
                item_line[field] = value

                response = self.post(self.url, item_line)

                self.assertEqual(400, response.status)

    def test_CreateItemLine_NameOver255Characters_ReturnsBadRequest(self):
        item_line = new_item_line()
        self.with_id(item_line)
        item_line["name"] = "x" * 256

        response = self.post(self.url, item_line)

        self.assertEqual(400, response.status)

    def test_UpdateItemLine_ChangesFieldsButKeepsCreatedAt(self):
        created = self.create(new_item_line())
        time.sleep(0.01)
        item_line = new_item_line(name="Updated Test Item Line")
        item_line["description"] = "Updated description"

        response = self.put(f"{self.url}/{created['id']}", item_line)

        self.assert_no_content(response)
        updated = self.get_json(f"{self.url}/{created['id']}")
        self.assertEqual("Updated Test Item Line", updated["name"])
        self.assertEqual("Updated description", updated["description"])
        self.assert_same_time(created["created_at"], updated.get("created_at"))
        self.assertGreater(parse_time(updated["updated_at"]), parse_time(created["updated_at"]))

    def test_UpdateItemLine_UnknownItemLine_ReturnsNotFound(self):
        response = self.put(f"{self.url}/999999", new_item_line())

        self.assertEqual(404, response.status)

    def test_UpdateItemLine_MissingField_ReturnsBadRequest(self):
        created = self.create(new_item_line())
        item_line = new_item_line()
        item_line["name"] = ""

        response = self.put(f"{self.url}/{created['id']}", item_line)

        self.assertEqual(400, response.status)

    def test_DeleteItemLine_RemovesTheItemLine(self):
        created = self.create(new_item_line())

        response = self.delete(f"{self.url}/{created['id']}")

        self.assert_no_content(response)
        self.assertEqual(404, self.get(f"{self.url}/{created['id']}").status)

    def test_DeleteItemLine_WithItems_ReturnsBadRequestAndKeepsTheItemLine(self):
        id = self.rows("item", limit=1)[0]["item_line_id"]

        response = self.delete(f"{self.url}/{id}")

        self.assertEqual(400, response.status)
        self.assertEqual(200, self.get(f"{self.url}/{id}").status)

    def test_DeleteItemLine_UnknownItemLine_ReturnsNotFound(self):
        response = self.delete(f"{self.url}/999999")

        self.assertEqual(404, response.status)
