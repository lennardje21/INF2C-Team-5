"""Python version of TransfersControllerTests.cs; runs against v1 or v2."""

import time
from datetime import timedelta

from support import ApiTestCase, parse_time, utc_now


def item(item_id, amount):
    return {"item_id": item_id, "amount": amount}


def new_transfer(from_location_id=1, to_location_id=2, items=()):
    return {
        "from_location_id": from_location_id,
        "to_location_id": to_location_id,
        "items": list(items),
    }


def invalid_transfers():
    return [
        ("no source location", new_transfer(from_location_id=0)),
        ("no destination location", new_transfer(to_location_id=0)),
        ("same location", new_transfer(from_location_id=1, to_location_id=1)),
        ("zero amount", new_transfer(items=[item(10, 0)])),
        ("duplicate item", new_transfer(items=[item(10, 1), item(10, 2)])),
        ("unknown item", new_transfer(items=[item(999999, 1)])),
    ]


class TransfersControllerTests(ApiTestCase):
    url = "/api/v1/transfers"
    file = "transfer"

    def inventory_quantity(self, item_id, location_id):
        return self.rows("inventory", item_id=item_id, location_id=location_id)[0]["quantity_on_hand"]

    def inventory_quantity_or_zero(self, item_id, location_id):
        rows = self.rows("inventory", item_id=item_id, location_id=location_id)
        return rows[0]["quantity_on_hand"] if rows else 0

    def transfer_item_count(self, transfer_id):
        return self.count("transfer_item", transfer_id=transfer_id)

    def test_GetTransfers_ReturnsSeededTransfersWithItems(self):
        transfers = self.get_json(self.url)

        self.assertIsNotNone(transfers)
        self.assertGreaterEqual(len(transfers), 800)
        self.assertTrue(any(t["items"] for t in transfers))

    def test_GetTransfer_ReturnsTransferWithItems(self):
        transfer = self.get_json(f"{self.url}/1")

        self.assertIsNotNone(transfer)
        self.assertEqual(1, transfer["id"])
        self.assertEqual("TRF-000001", transfer["reference"])
        self.assertEqual(369, transfer["from_location_id"])
        self.assertEqual(320, transfer["to_location_id"])
        self.assertEqual("Scheduled", transfer["transfer_status"])
        self.assertTrue(transfer["items"])

    def test_GetTransfer_UnknownTransfer_ReturnsNotFound(self):
        for suffix in ["", "/items"]:
            with self.subTest(suffix=suffix):
                response = self.get(f"{self.url}/2147483647{suffix}")

                self.assertEqual(404, response.status)

    def test_GetTransferItems_ReturnsItemsForTransfer(self):
        items = self.get_json(f"{self.url}/1/items")

        self.assertIsNotNone(items)
        self.assertEqual(self.transfer_item_count(1), len(items))
        for i in items:
            self.assertEqual(1, i.get("transfer_id"))

    def test_CreateTransfer_StoresTransferAndItems(self):
        before = utc_now() - timedelta(seconds=1)

        created = self.create(new_transfer(items=[item(10, 2)]))

        self.assertGreater(created["id"], 800)
        self.assertEqual(f"TRF-{created['id']:06d}", created.get("reference"))
        self.assertEqual("Scheduled", created["transfer_status"])
        self.assertGreaterEqual(parse_time(created["created_at"]), before)
        self.assert_same_time(created["created_at"], created["updated_at"])
        self.assertEqual([(10, 2)], [(i["item_id"], i["amount"]) for i in created["items"]])

    def test_CreateTransfer_InvalidInput_ReturnsBadRequest(self):
        for name, transfer in invalid_transfers():
            with self.subTest(name):
                self.with_id(transfer)
                count = self.count(self.file)

                response = self.post(self.url, transfer)

                self.assertEqual(400, response.status)
                self.assertEqual(count, self.count(self.file))

    def test_UpdateTransfer_ChangesFieldsButKeepsReferenceAndCreatedAt(self):
        created = self.create(new_transfer(items=[item(10, 2)]))
        time.sleep(0.01)

        changes = new_transfer(from_location_id=3, to_location_id=4, items=[item(11, 3)])
        changes["reference"] = "TRF-CHANGED"
        changes["transfer_status"] = "In Transit"

        response = self.put(f"{self.url}/{created['id']}", changes)

        self.assert_no_content(response)
        updated = self.get_json(f"{self.url}/{created['id']}")
        self.assertEqual(f"TRF-{created['id']:06d}", updated["reference"])
        self.assertEqual("In Transit", updated["transfer_status"])
        self.assertEqual((3, 4), (updated["from_location_id"], updated["to_location_id"]))
        self.assert_same_time(created["created_at"], updated.get("created_at"))
        self.assertGreater(parse_time(updated["updated_at"]), parse_time(created["updated_at"]))
        self.assertEqual([(11, 3)], [(i["item_id"], i["amount"]) for i in updated["items"]])

    def test_UpdateTransfer_UnknownTransfer_ReturnsNotFound(self):
        response = self.put(f"{self.url}/2147483647", new_transfer())

        self.assertEqual(404, response.status)

    def test_CommitTransfer_MovesInventoryAndMarksTransferProcessed(self):
        source = max(
            (r for r in self.rows("inventory", item_id=10) if r["quantity_on_hand"] > 0),
            key=lambda r: r["quantity_on_hand"])["location_id"]
        destination = next(x["id"] for x in self.rows("location") if x["id"] != source)
        created = self.create(new_transfer(source, destination, [item(10, 1)]))
        source_before = self.inventory_quantity(10, source)
        destination_before = self.inventory_quantity_or_zero(10, destination)

        response = self.put(f"{self.url}/{created['id']}/commit", None)

        self.assert_no_content(response)
        processed = self.get_json(f"{self.url}/{created['id']}")
        self.assertEqual("Processed", processed["transfer_status"])
        self.assertEqual(source_before - 1, self.inventory_quantity(10, source))
        self.assertEqual(destination_before + 1, self.inventory_quantity_or_zero(10, destination))

    def test_CommitTransfer_AlreadyProcessed_ReturnsBadRequest(self):
        response = self.put(f"{self.url}/2/commit", None)

        self.assertEqual(400, response.status)

    def test_CommitTransfer_UnknownTransfer_ReturnsNotFound(self):
        response = self.put(f"{self.url}/2147483647/commit", None)

        self.assertEqual(404, response.status)

    def test_DeleteTransfer_RemovesTransferAndItems(self):
        created = self.create(new_transfer(items=[item(12, 1)]))

        response = self.delete(f"{self.url}/{created['id']}")

        self.assert_no_content(response)
        self.assertEqual(404, self.get(f"{self.url}/{created['id']}").status)
        self.assertEqual(0, self.transfer_item_count(created["id"]))

    def test_DeleteTransfer_UnknownTransfer_ReturnsNotFound(self):
        response = self.delete(f"{self.url}/2147483647")

        self.assertEqual(404, response.status)
