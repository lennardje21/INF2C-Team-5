"""Python version of AuthenticationTests.cs; runs against v1 or v2."""

from support import ApiTestCase


class AuthenticationTests(ApiTestCase):
    def test_MissingApiKey_ReturnsUnauthorized(self):
        response = self.get("/api/v1/warehouses", api_key=None)

        self.assertEqual(401, response.status)

    def test_InvalidApiKey_ReturnsUnauthorized(self):
        response = self.get("/api/v1/warehouses", api_key="invalid-test-key")

        self.assertEqual(401, response.status)

    def test_ReadOnlyApiKey_CanReadButCannotCreate(self):
        key = self.rows("user", app="analytics_dashboard")[0]["api_key"]

        read = self.get("/api/v1/warehouses", api_key=key)
        self.assertEqual(200, read.status)
        create = self.request("POST", "/api/v1/warehouses", api_key=key, raw_body=b"")
        self.assertEqual(403, create.status)
