-- CargoHub MySQL 8.4 schema, aligned with the C# models.
-- Apply only to an empty database; DDL is not transactional in MySQL.
SET NAMES utf8mb4;

CREATE TABLE `warehouse` (
    `id` INT NOT NULL AUTO_INCREMENT,
    `code` VARCHAR(255) NOT NULL,
    `name` VARCHAR(255) NOT NULL,
    `address` VARCHAR(255) NOT NULL,
    `city` VARCHAR(255) NOT NULL,
    `zip_code` VARCHAR(255) NOT NULL,
    `province` VARCHAR(255) NOT NULL,
    `country` VARCHAR(255) NOT NULL,
    `contact_name` VARCHAR(255) NOT NULL,
    `contact_phone` VARCHAR(255) NOT NULL,
    `contact_email` VARCHAR(255) NOT NULL,
    `created_at` DATETIME(6) NOT NULL,
    `updated_at` DATETIME(6) NOT NULL,
    PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `client` (
    `id` INT NOT NULL AUTO_INCREMENT,
    `name` VARCHAR(255) NOT NULL,
    `address` VARCHAR(255) NOT NULL,
    `city` VARCHAR(255) NOT NULL,
    `zip_code` VARCHAR(255) NOT NULL,
    `province` VARCHAR(255) NOT NULL,
    `country` VARCHAR(255) NOT NULL,
    `contact_name` VARCHAR(255) NOT NULL,
    `contact_phone` VARCHAR(255) NOT NULL,
    `contact_email` VARCHAR(255) NOT NULL,
    `created_at` DATETIME(6) NOT NULL,
    `updated_at` DATETIME(6) NOT NULL,
    PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `supplier` (
    `id` INT NOT NULL AUTO_INCREMENT,
    `code` VARCHAR(255) NOT NULL,
    `name` VARCHAR(255) NOT NULL,
    `address` VARCHAR(255) NOT NULL,
    `city` VARCHAR(255) NOT NULL,
    `zip_code` VARCHAR(255) NOT NULL,
    `province` VARCHAR(255) NOT NULL,
    `country` VARCHAR(255) NOT NULL,
    `contact_name` VARCHAR(255) NOT NULL,
    `phone_number` VARCHAR(255) NOT NULL,
    `reference` VARCHAR(255) NOT NULL,
    `created_at` DATETIME(6) NOT NULL,
    `updated_at` DATETIME(6) NOT NULL,
    PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `item_line` (
    `id` INT NOT NULL AUTO_INCREMENT,
    `name` VARCHAR(255) NOT NULL,
    `description` TEXT NOT NULL,
    `created_at` DATETIME(6) NOT NULL,
    `updated_at` DATETIME(6) NOT NULL,
    PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `item_group` (
    `id` INT NOT NULL AUTO_INCREMENT,
    `name` VARCHAR(255) NOT NULL,
    `description` TEXT NOT NULL,
    `created_at` DATETIME(6) NOT NULL,
    `updated_at` DATETIME(6) NOT NULL,
    PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `item_type` (
    `id` INT NOT NULL AUTO_INCREMENT,
    `name` VARCHAR(255) NOT NULL,
    `description` TEXT NOT NULL,
    `created_at` DATETIME(6) NOT NULL,
    `updated_at` DATETIME(6) NOT NULL,
    PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `location` (
    `id` INT NOT NULL AUTO_INCREMENT,
    `warehouse_id` INT NOT NULL,
    `code` VARCHAR(255) NOT NULL,
    `name` VARCHAR(255) NOT NULL,
    `created_at` DATETIME(6) NOT NULL,
    `updated_at` DATETIME(6) NOT NULL,
    PRIMARY KEY (`id`),
    FOREIGN KEY (`warehouse_id`) REFERENCES `warehouse` (`id`) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `item` (
    `id` INT NOT NULL AUTO_INCREMENT,
    `code` VARCHAR(255) NOT NULL,
    `description` TEXT NOT NULL,
    `barcode` VARCHAR(255) NOT NULL,
    `model_number` VARCHAR(255) NOT NULL,
    `commodity_code` INT NOT NULL,
    `unit_weight` DECIMAL(18,6) NOT NULL,
    `item_line_id` INT NOT NULL,
    `item_group_id` INT NOT NULL,
    `item_type_id` INT NOT NULL,
    `min_purchase_qty` INT NOT NULL,
    `case_size` INT NOT NULL,
    `packaging_type` VARCHAR(255) NOT NULL,
    `order_multiple` INT NOT NULL,
    `supplier_id` INT NOT NULL,
    `supplier_sku` VARCHAR(255) NOT NULL,
    `created_at` DATETIME(6) NOT NULL,
    `updated_at` DATETIME(6) NOT NULL,
    PRIMARY KEY (`id`),
    FOREIGN KEY (`item_line_id`) REFERENCES `item_line` (`id`) ON DELETE RESTRICT,
    FOREIGN KEY (`item_group_id`) REFERENCES `item_group` (`id`) ON DELETE RESTRICT,
    FOREIGN KEY (`item_type_id`) REFERENCES `item_type` (`id`) ON DELETE RESTRICT,
    FOREIGN KEY (`supplier_id`) REFERENCES `supplier` (`id`) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `inventory` (
    `item_id` INT NOT NULL,
    `location_id` INT NOT NULL,
    `quantity_on_hand` INT NOT NULL,
    `quantity_expected` INT NOT NULL,
    `quantity_ordered` INT NOT NULL,
    `quantity_allocated` INT NOT NULL,
    `created_at` DATETIME(6) NOT NULL,
    `updated_at` DATETIME(6) NOT NULL,
    PRIMARY KEY (`item_id`, `location_id`),
    FOREIGN KEY (`item_id`) REFERENCES `item` (`id`) ON DELETE RESTRICT,
    FOREIGN KEY (`location_id`) REFERENCES `location` (`id`) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `order` (
    `id` INT NOT NULL AUTO_INCREMENT,
    `client_id` INT NOT NULL,
    `order_date` DATETIME(6) NOT NULL,
    `request_date` DATETIME(6) NOT NULL,
    `reference` VARCHAR(255) NOT NULL,
    `customer_po_number` VARCHAR(255) NOT NULL,
    `order_status` VARCHAR(255) NOT NULL,
    `shipping_notes` TEXT NULL,
    `warehouse_id` INT NOT NULL,
    `ship_to_client_id` INT NOT NULL,
    `bill_to_client_id` INT NOT NULL,
    `created_at` DATETIME(6) NOT NULL,
    `updated_at` DATETIME(6) NOT NULL,
    PRIMARY KEY (`id`),
    FOREIGN KEY (`client_id`) REFERENCES `client` (`id`) ON DELETE RESTRICT,
    FOREIGN KEY (`warehouse_id`) REFERENCES `warehouse` (`id`) ON DELETE RESTRICT,
    FOREIGN KEY (`ship_to_client_id`) REFERENCES `client` (`id`) ON DELETE RESTRICT,
    FOREIGN KEY (`bill_to_client_id`) REFERENCES `client` (`id`) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `order_item` (
    `id` INT NOT NULL AUTO_INCREMENT,
    `order_id` INT NOT NULL,
    `item_id` INT NOT NULL,
    `amount` INT NOT NULL,
    `unit_price` DECIMAL(18,4) NOT NULL,
    PRIMARY KEY (`id`),
    FOREIGN KEY (`order_id`) REFERENCES `order` (`id`) ON DELETE RESTRICT,
    FOREIGN KEY (`item_id`) REFERENCES `item` (`id`) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `shipment` (
    `id` INT NOT NULL AUTO_INCREMENT,
    `reference` VARCHAR(255) NOT NULL,
    `order_id` INT NOT NULL,
    `shipment_date` DATETIME(6) NOT NULL,
    `shipment_type` VARCHAR(255) NOT NULL,
    `shipment_status` VARCHAR(255) NOT NULL,
    `carrier_name` VARCHAR(255) NOT NULL,
    `shipping_method` VARCHAR(255) NOT NULL,
    `payment_type` VARCHAR(255) NOT NULL,
    `created_at` DATETIME(6) NOT NULL,
    `updated_at` DATETIME(6) NOT NULL,
    PRIMARY KEY (`id`),
    FOREIGN KEY (`order_id`) REFERENCES `order` (`id`) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `shipment_item` (
    `id` INT NOT NULL AUTO_INCREMENT,
    `shipment_id` INT NOT NULL,
    `item_id` INT NOT NULL,
    `amount` INT NOT NULL,
    PRIMARY KEY (`id`),
    FOREIGN KEY (`shipment_id`) REFERENCES `shipment` (`id`) ON DELETE RESTRICT,
    FOREIGN KEY (`item_id`) REFERENCES `item` (`id`) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `transfer` (
    `id` INT NOT NULL AUTO_INCREMENT,
    `reference` VARCHAR(255) NOT NULL,
    `from_location_id` INT NOT NULL,
    `to_location_id` INT NOT NULL,
    `transfer_status` VARCHAR(255) NOT NULL,
    `created_at` DATETIME(6) NOT NULL,
    `updated_at` DATETIME(6) NOT NULL,
    PRIMARY KEY (`id`),
    FOREIGN KEY (`from_location_id`) REFERENCES `location` (`id`) ON DELETE RESTRICT,
    FOREIGN KEY (`to_location_id`) REFERENCES `location` (`id`) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `transfer_item` (
    `id` INT NOT NULL AUTO_INCREMENT,
    `transfer_id` INT NOT NULL,
    `item_id` INT NOT NULL,
    `amount` INT NOT NULL,
    PRIMARY KEY (`id`),
    FOREIGN KEY (`transfer_id`) REFERENCES `transfer` (`id`) ON DELETE RESTRICT,
    FOREIGN KEY (`item_id`) REFERENCES `item` (`id`) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- Everyone and everything that uses the API has an account with exactly one API key.
-- Roles from high to low: superroot, root, admin, floormanager, user. The superroot is not bound to a
-- warehouse; every other account belongs to exactly one.
-- Only the SHA-256 hash of the key is stored (lowercase hex, as MySQL's SHA2() returns);
-- NULL means the account has no key yet (see scripts/set-api-key.sh).
CREATE TABLE `account` (
    `id` INT NOT NULL AUTO_INCREMENT,
    `role` VARCHAR(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `name` VARCHAR(255) NOT NULL,
    `email` VARCHAR(255) NOT NULL,
    `warehouse_id` INT NULL,
    `app` VARCHAR(255) NOT NULL,
    `api_key_hash` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NULL,
    `created_at` DATETIME(6) NOT NULL,
    `updated_at` DATETIME(6) NOT NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uq_account_email` (`email`),
    UNIQUE KEY `uq_account_api_key_hash` (`api_key_hash`),
    CONSTRAINT `chk_account_role` CHECK (`role` IN ('superroot', 'root', 'admin', 'floormanager', 'user')),
    CHECK ((`role` = 'superroot') = (`warehouse_id` IS NULL)),
    FOREIGN KEY (`warehouse_id`) REFERENCES `warehouse` (`id`) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `account_endpoint` (
    `account_id` INT NOT NULL,
    `resource` VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
    PRIMARY KEY (`account_id`, `resource`),
    FOREIGN KEY (`account_id`) REFERENCES `account` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `account_permission` (
    `account_id` INT NOT NULL,
    `resource` VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
    `method` VARCHAR(16) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
    `allowed` BOOLEAN NOT NULL,
    PRIMARY KEY (`account_id`, `resource`, `method`),
    CHECK (`allowed` IN (0, 1)),
    FOREIGN KEY (`account_id`, `resource`) REFERENCES `account_endpoint` (`account_id`, `resource`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `schema_version` (
    `version` INT NOT NULL PRIMARY KEY
) ENGINE=InnoDB;

-- Blockade detection (FR-7). blockade_setting holds the times an admin or root set through
-- the API; without a row the defaults from appsettings.json apply. blockade_notification
-- records which blockades were already mailed: the item and its updated_at at that time,
-- so an item that moves on and gets stuck again counts as a new blockade.
CREATE TABLE `blockade_setting` (
    `id` TINYINT NOT NULL,
    `order_max_minutes` INT NOT NULL,
    `shipment_max_minutes` INT NOT NULL,
    `transfer_max_minutes` INT NOT NULL,
    `check_interval_minutes` INT NOT NULL,
    `updated_at` DATETIME(6) NOT NULL,
    PRIMARY KEY (`id`),
    CHECK (`id` = 1),
    CHECK (`order_max_minutes` BETWEEN 1 AND 525600),
    CHECK (`shipment_max_minutes` BETWEEN 1 AND 525600),
    CHECK (`transfer_max_minutes` BETWEEN 1 AND 525600),
    CHECK (`check_interval_minutes` BETWEEN 1 AND 1440)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `blockade_notification` (
    `item_type` VARCHAR(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `item_id` INT NOT NULL,
    `item_updated_at` DATETIME(6) NOT NULL,
    `notified_at` DATETIME(6) NOT NULL,
    PRIMARY KEY (`item_type`, `item_id`, `item_updated_at`),
    CHECK (`item_type` IN ('order', 'shipment', 'transfer'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

INSERT INTO `schema_version` (`version`) VALUES (3);
