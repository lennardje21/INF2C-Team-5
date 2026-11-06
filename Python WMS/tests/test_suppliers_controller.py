"""Python version of SuppliersControllerTests.cs; runs against v1 or v2."""

import time
from datetime import timedelta

from support import ApiTestCase, parse_time, utc_now


def new_supplier(name="Jumbo Test"):
    return {
        "name": name,
        "address": "Teststraat 1",
        "city": "Rotterdam",
        "zip_code": "3011AA",
        "province": "Zuid-Holland",
        "country": "Netherlands",
        "contact_name": "Test Persoon",
        "phone_number": "010-1234567",
        "code": "SUP-TEST",
        "reference": "TEST-REF",
    }


class SuppliersControllerTests(ApiTestCase):
    url = "/api/v1/suppliers"
    file = "supplier"

    def test_GetSuppliers_ReturnsSeededSuppliers(self):
        suppliers = self.get_json(self.url)
        expected = self.count(self.file)

        self.assertIsNotNone(suppliers)
        self.assertEqual(expected, len(suppliers))
        self.assertTrue(suppliers)

    def test_GetSupplier_ReturnsTheSupplier(self):
        created = self.create(new_supplier())
        supplier = self.get_json(f"{self.url}/{created['id']}")

        self.assertIsNotNone(supplier)
        self.assertEqual(created["id"], supplier["id"])
        self.assertEqual(created["name"], supplier["name"])
        self.assert_same_time(created["created_at"], supplier["created_at"])

    def test_GetSupplier_UnknownSupplier_ReturnsNotFound(self):
        response = self.get(f"{self.url}/999999")

        self.assertEqual(404, response.status)

    def test_CreateSupplier_StoresTheSupplier(self):
        supplier = new_supplier()
        self.with_id(supplier)
        before = utc_now() - timedelta(seconds=1)

        response = self.post(self.url, supplier)

        created = self.created_body(response)
        self.assertEqual(f"{self.url}/{created['id']}", response.location_path())
        self.assertGreater(created["id"], 0)
        self.assertGreaterEqual(parse_time(created["created_at"]), before)
        self.assert_same_time(created["created_at"], created["updated_at"])

        stored = self.get_json(f"{self.url}/{created['id']}")
        self.assertEqual("Jumbo Test", stored["name"])
        self.assertEqual("010-1234567", stored["phone_number"])
        self.assertEqual("SUP-TEST", stored["code"])
        self.assertEqual("TEST-REF", stored["reference"])

    def test_CreateSupplier_IgnoresIdAndTimestampsFromTheRequest(self):
        supplier = new_supplier()
        supplier["id"] = 1
        supplier["created_at"] = "2000-01-01T00:00:00Z"

        created = self.created_body(self.post(self.url, supplier))

        self.assertNotEqual(1, created["id"])
        self.assertNotEqual(2000, parse_time(created["created_at"]).year)

    def test_CreateSupplier_MissingField_ReturnsBadRequest(self):
        cases = [("name", ""), ("city", "  "), ("address", ""), ("zip_code", ""), ("province", ""),
                 ("country", ""), ("contact_name", ""), ("phone_number", ""), ("code", ""), ("reference", "")]
        for field, value in cases:
            with self.subTest(field=field, value=value):
                supplier = new_supplier()
                self.with_id(supplier)
                supplier[field] = value

                response = self.post(self.url, supplier)

                self.assertEqual(400, response.status)

    def test_UpdateSupplier_ChangesFieldsButKeepsCreatedAt(self):
        created = self.create(new_supplier())
        time.sleep(0.01)

        response = self.put(f"{self.url}/{created['id']}", new_supplier(name="Jumbo Changed"))

        self.assert_no_content(response)
        updated = self.get_json(f"{self.url}/{created['id']}")
        self.assertEqual("Jumbo Changed", updated["name"])
        self.assert_same_time(created["created_at"], updated.get("created_at"))
        self.assertGreater(parse_time(updated["updated_at"]), parse_time(created["updated_at"]))

    def test_UpdateSupplier_UnknownSupplier_ReturnsNotFound(self):
        response = self.put(f"{self.url}/999999", new_supplier())

        self.assertEqual(404, response.status)

    def test_UpdateSupplier_MissingField_ReturnsBadRequest(self):
        created = self.create(new_supplier())

        response = self.put(f"{self.url}/{created['id']}", new_supplier(name=""))

        self.assertEqual(400, response.status)

    def test_DeleteSupplier_RemovesTheSupplier(self):
        created = self.create(new_supplier())

        response = self.delete(f"{self.url}/{created['id']}")

        self.assert_no_content(response)
        self.assertEqual(404, self.get(f"{self.url}/{created['id']}").status)

    def test_DeleteSupplier_WithItems_ReturnsConflictAndKeepsTheSupplier(self):
        id = self.rows("item", limit=1)[0]["supplier_id"]

        response = self.delete(f"{self.url}/{id}")

        self.assertEqual(409, response.status)
        self.assertEqual(200, self.get(f"{self.url}/{id}").status)

    def test_DeleteSupplier_UnknownSupplier_ReturnsNotFound(self):
        response = self.delete(f"{self.url}/999999")

        self.assertEqual(404, response.status)
