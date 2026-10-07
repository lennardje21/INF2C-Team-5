-- CargoHUB SQLite schema v1. Apply to an empty database.
PRAGMA foreign_keys = ON;

CREATE TABLE "warehouse" (
    "id" INTEGER PRIMARY KEY,
    "code" TEXT NOT NULL,
    "name" TEXT NOT NULL,
    "address" TEXT NOT NULL,
    "city" TEXT NOT NULL,
    "zip_code" TEXT NOT NULL,
    "province" TEXT NOT NULL,
    "country" TEXT NOT NULL,
    "contact_name" TEXT NOT NULL,
    "contact_phone" TEXT NOT NULL,
    "contact_email" TEXT NOT NULL,
    "created_at" TEXT NOT NULL,
    "updated_at" TEXT NOT NULL
);

CREATE TABLE "client" (
    "id" INTEGER PRIMARY KEY,
    "name" TEXT NOT NULL,
    "address" TEXT NOT NULL,
    "city" TEXT NOT NULL,
    "zip_code" TEXT NOT NULL,
    "province" TEXT NOT NULL,
    "country" TEXT NOT NULL,
    "contact_name" TEXT NOT NULL,
    "contact_phone" TEXT NOT NULL,
    "contact_email" TEXT NOT NULL,
    "created_at" TEXT NOT NULL,
    "updated_at" TEXT NOT NULL
);

CREATE TABLE "supplier" (
    "id" INTEGER PRIMARY KEY,
    "code" TEXT NOT NULL,
    "name" TEXT NOT NULL,
    "address" TEXT NOT NULL,
    "city" TEXT NOT NULL,
    "zip_code" TEXT NOT NULL,
    "province" TEXT NOT NULL,
    "country" TEXT NOT NULL,
    "contact_name" TEXT NOT NULL,
    "phone_number" TEXT NOT NULL,
    "reference" TEXT NOT NULL,
    "created_at" TEXT NOT NULL,
    "updated_at" TEXT NOT NULL
);

CREATE TABLE "item_line" (
    "id" INTEGER PRIMARY KEY,
    "name" TEXT NOT NULL,
    "description" TEXT NOT NULL,
    "created_at" TEXT NOT NULL,
    "updated_at" TEXT NOT NULL
);

CREATE TABLE "item_group" (
    "id" INTEGER PRIMARY KEY,
    "name" TEXT NOT NULL,
    "description" TEXT NOT NULL,
    "created_at" TEXT NOT NULL,
    "updated_at" TEXT NOT NULL
);

CREATE TABLE "item_type" (
    "id" INTEGER PRIMARY KEY,
    "name" TEXT NOT NULL,
    "description" TEXT NOT NULL,
    "created_at" TEXT NOT NULL,
    "updated_at" TEXT NOT NULL
);

CREATE TABLE "location" (
    "id" INTEGER PRIMARY KEY,
    "warehouse_id" INTEGER NOT NULL REFERENCES "warehouse"("id") ON DELETE RESTRICT,
    "code" TEXT NOT NULL,
    "name" TEXT NOT NULL,
    "created_at" TEXT NOT NULL,
    "updated_at" TEXT NOT NULL
);

CREATE TABLE "item" (
    "id" INTEGER PRIMARY KEY,
    "code" TEXT NOT NULL,
    "description" TEXT NOT NULL,
    "barcode" TEXT NOT NULL,
    "model_number" TEXT NOT NULL,
    "commodity_code" INTEGER NOT NULL,
    "unit_weight" REAL NOT NULL,
    "item_line_id" INTEGER NOT NULL REFERENCES "item_line"("id") ON DELETE RESTRICT,
    "item_group_id" INTEGER NOT NULL REFERENCES "item_group"("id") ON DELETE RESTRICT,
    "item_type_id" INTEGER NOT NULL REFERENCES "item_type"("id") ON DELETE RESTRICT,
    "min_purchase_qty" INTEGER NOT NULL,
    "case_size" INTEGER NOT NULL,
    "packaging_type" TEXT NOT NULL,
    "order_multiple" INTEGER NOT NULL,
    "supplier_id" INTEGER NOT NULL REFERENCES "supplier"("id") ON DELETE RESTRICT,
    "supplier_sku" TEXT NOT NULL,
    "created_at" TEXT NOT NULL,
    "updated_at" TEXT NOT NULL
);

CREATE TABLE "inventory" (
    "item_id" INTEGER NOT NULL REFERENCES "item"("id") ON DELETE RESTRICT,
    "location_id" INTEGER NOT NULL REFERENCES "location"("id") ON DELETE RESTRICT,
    "quantity_on_hand" INTEGER NOT NULL,
    "quantity_expected" INTEGER NOT NULL,
    "quantity_ordered" INTEGER NOT NULL,
    "quantity_allocated" INTEGER NOT NULL,
    "created_at" TEXT NOT NULL,
    "updated_at" TEXT NOT NULL,
    PRIMARY KEY ("item_id", "location_id")
);

CREATE TABLE "order" (
    "id" INTEGER PRIMARY KEY,
    "client_id" INTEGER NOT NULL REFERENCES "client"("id") ON DELETE RESTRICT,
    "order_date" TEXT NOT NULL,
    "request_date" TEXT NOT NULL,
    "reference" TEXT NOT NULL,
    "customer_po_number" TEXT NOT NULL,
    "order_status" TEXT NOT NULL,
    "shipping_notes" TEXT,
    "warehouse_id" INTEGER NOT NULL REFERENCES "warehouse"("id") ON DELETE RESTRICT,
    "ship_to_client_id" INTEGER NOT NULL REFERENCES "client"("id") ON DELETE RESTRICT,
    "bill_to_client_id" INTEGER NOT NULL REFERENCES "client"("id") ON DELETE RESTRICT,
    "created_at" TEXT NOT NULL,
    "updated_at" TEXT NOT NULL
);

CREATE TABLE "order_item" (
    "id" INTEGER PRIMARY KEY,
    "order_id" INTEGER NOT NULL REFERENCES "order"("id") ON DELETE RESTRICT,
    "item_id" INTEGER NOT NULL REFERENCES "item"("id") ON DELETE RESTRICT,
    "amount" INTEGER NOT NULL,
    "unit_price" REAL NOT NULL
);

CREATE TABLE "shipment" (
    "id" INTEGER PRIMARY KEY,
    "reference" TEXT NOT NULL,
    "order_id" INTEGER NOT NULL REFERENCES "order"("id") ON DELETE RESTRICT,
    "shipment_date" TEXT NOT NULL,
    "shipment_type" TEXT NOT NULL,
    "shipment_status" TEXT NOT NULL,
    "carrier_name" TEXT NOT NULL,
    "shipping_method" TEXT NOT NULL,
    "payment_type" TEXT NOT NULL,
    "created_at" TEXT NOT NULL,
    "updated_at" TEXT NOT NULL
);

CREATE TABLE "shipment_item" (
    "id" INTEGER PRIMARY KEY,
    "shipment_id" INTEGER NOT NULL REFERENCES "shipment"("id") ON DELETE RESTRICT,
    "item_id" INTEGER NOT NULL REFERENCES "item"("id") ON DELETE RESTRICT,
    "amount" INTEGER NOT NULL
);

CREATE TABLE "transfer" (
    "id" INTEGER PRIMARY KEY,
    "reference" TEXT NOT NULL,
    "from_location_id" INTEGER NOT NULL REFERENCES "location"("id") ON DELETE RESTRICT,
    "to_location_id" INTEGER NOT NULL REFERENCES "location"("id") ON DELETE RESTRICT,
    "transfer_status" TEXT NOT NULL,
    "created_at" TEXT NOT NULL,
    "updated_at" TEXT NOT NULL
);

CREATE TABLE "transfer_item" (
    "id" INTEGER PRIMARY KEY,
    "transfer_id" INTEGER NOT NULL REFERENCES "transfer"("id") ON DELETE RESTRICT,
    "item_id" INTEGER NOT NULL REFERENCES "item"("id") ON DELETE RESTRICT,
    "amount" INTEGER NOT NULL
);

CREATE TABLE "user" (
    "api_key" TEXT PRIMARY KEY,
    "app" TEXT NOT NULL
);

CREATE TABLE user_endpoint (
    api_key TEXT NOT NULL REFERENCES "user"(api_key) ON DELETE CASCADE,
    resource TEXT NOT NULL,
    PRIMARY KEY (api_key, resource)
);
CREATE TABLE user_permission (
    api_key TEXT NOT NULL,
    resource TEXT NOT NULL,
    method TEXT NOT NULL,
    allowed INTEGER NOT NULL CHECK (allowed IN (0, 1)),
    PRIMARY KEY (api_key, resource, method),
    FOREIGN KEY (api_key, resource) REFERENCES user_endpoint(api_key, resource) ON DELETE CASCADE
);

CREATE INDEX "idx_location_warehouse_id" ON "location"("warehouse_id");
CREATE INDEX "idx_item_item_line_id" ON "item"("item_line_id");
CREATE INDEX "idx_item_item_group_id" ON "item"("item_group_id");
CREATE INDEX "idx_item_item_type_id" ON "item"("item_type_id");
CREATE INDEX "idx_item_supplier_id" ON "item"("supplier_id");
CREATE INDEX "idx_inventory_location_id" ON "inventory"("location_id");
CREATE INDEX "idx_order_client_id" ON "order"("client_id");
CREATE INDEX "idx_order_warehouse_id" ON "order"("warehouse_id");
CREATE INDEX "idx_order_ship_to_client_id" ON "order"("ship_to_client_id");
CREATE INDEX "idx_order_bill_to_client_id" ON "order"("bill_to_client_id");
CREATE INDEX "idx_order_item_order_id" ON "order_item"("order_id");
CREATE INDEX "idx_order_item_item_id" ON "order_item"("item_id");
CREATE INDEX "idx_shipment_order_id" ON "shipment"("order_id");
CREATE INDEX "idx_shipment_item_shipment_id" ON "shipment_item"("shipment_id");
CREATE INDEX "idx_shipment_item_item_id" ON "shipment_item"("item_id");
CREATE INDEX "idx_transfer_from_location_id" ON "transfer"("from_location_id");
CREATE INDEX "idx_transfer_to_location_id" ON "transfer"("to_location_id");
CREATE INDEX "idx_transfer_item_transfer_id" ON "transfer_item"("transfer_id");
CREATE INDEX "idx_transfer_item_item_id" ON "transfer_item"("item_id");
PRAGMA user_version = 1;
