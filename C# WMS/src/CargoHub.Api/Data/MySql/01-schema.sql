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

CREATE TABLE `user` (
    `api_key` VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
    `app` VARCHAR(255) NOT NULL,
    PRIMARY KEY (`api_key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `user_endpoint` (
    `api_key` VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
    `resource` VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
    PRIMARY KEY (`api_key`, `resource`),
    FOREIGN KEY (`api_key`) REFERENCES `user` (`api_key`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
CREATE TABLE `user_permission` (
    `api_key` VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
    `resource` VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
    `method` VARCHAR(16) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
    `allowed` BOOLEAN NOT NULL,
    PRIMARY KEY (`api_key`, `resource`, `method`),
    CHECK (`allowed` IN (0, 1)),
    FOREIGN KEY (`api_key`, `resource`) REFERENCES `user_endpoint` (`api_key`, `resource`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
CREATE TABLE `schema_version` (
    `version` INT NOT NULL PRIMARY KEY
) ENGINE=InnoDB;
INSERT INTO `schema_version` (`version`) VALUES (1);
