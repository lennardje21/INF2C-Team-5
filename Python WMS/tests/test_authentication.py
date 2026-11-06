"""v1 counterpart of AuthenticationTests.cs."""

from support import ApiTestCase


class AuthenticationTests(ApiTestCase):
    def test_MissingApiKey_ReturnsUnauthorized(self):
        response = self.get("/api/v1/warehouses", api_key=None)

        self.assertEqual(401, response.status)

    def test_InvalidApiKey_ReturnsUnauthorized(self):
        response = self.get("/api/v1/warehouses", api_key="invalid-test-key")

        self.assertEqual(401, response.status)

    def test_ReadOnlyApiKey_CanReadButCannotCreate(self):
        key = next(u["api_key"] for u in self.records("user") if u["app"] == "analytics_dashboard")

        read = self.get("/api/v1/warehouses", api_key=key)
        self.assertEqual(200, read.status)
        create = self.request("POST", "/api/v1/warehouses", api_key=key, raw_body=b"")
        self.assertEqual(403, create.status)
