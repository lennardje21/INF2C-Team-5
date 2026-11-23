-- Version 1 -> 2: API keys belong to accounts. A clean start: the old users and their API
-- keys are removed (the keys are public on GitHub). Like 02-seed.sql, this creates the
-- superroot and a root, admin and user for every existing warehouse, all without an API
-- key: give the superroot one with scripts/set-api-key.sh afterwards.
--
-- deploy/deploy.sh applies this automatically (after a backup) when it deploys the first
-- image that contains it. It needs the mysql client, which stops at the first error.
-- If it fails halfway, it can simply be run again: it only continues on version 1 and
-- first removes what an earlier attempt created.
SET NAMES utf8mb4;
SET time_zone = '+00:00';

DROP PROCEDURE IF EXISTS `check_002_accounts`;
DELIMITER //
CREATE PROCEDURE `check_002_accounts`()
BEGIN
    IF (SELECT MAX(`version`) FROM `schema_version`) <> 1 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Migration 002 needs schema_version 1.';
    END IF;
    -- Account emails are made from the warehouse codes and must be unique.
    IF EXISTS (SELECT 1 FROM `warehouse` GROUP BY LOWER(`code`) HAVING COUNT(*) > 1) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Migration 002 needs unique warehouse codes.';
    END IF;
END //
DELIMITER ;
CALL `check_002_accounts`();
DROP PROCEDURE `check_002_accounts`;

DROP TABLE IF EXISTS `account_permission`, `account_endpoint`, `account`;

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
    CHECK (`role` IN ('superroot', 'root', 'admin', 'user')),
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

START TRANSACTION;

SET @now = UTC_TIMESTAMP(6);

INSERT INTO `account` (`id`, `role`, `name`, `email`, `warehouse_id`, `app`, `api_key_hash`, `created_at`, `updated_at`)
VALUES (1, 'superroot', 'CargoHub Superroot', 'superroot@cargohub.nl', NULL, 'account_management', NULL, @now, @now);

INSERT INTO `account` (`role`, `name`, `email`, `warehouse_id`, `app`, `api_key_hash`, `created_at`, `updated_at`)
SELECT r.`role`, CONCAT(r.`title`, ' ', w.`code`), CONCAT(r.`role`, '.', LOWER(w.`code`), '@cargohub.nl'),
       w.`id`, r.`app`, NULL, @now, @now
FROM `warehouse` w
CROSS JOIN (
    SELECT 1 AS `position`, 'root' AS `role`, 'Root' AS `title`, 'warehouse_portal' AS `app`
    UNION ALL SELECT 2, 'admin', 'Admin', 'warehouse_portal'
    UNION ALL SELECT 3, 'user', 'User', 'handheld_scanner') r
ORDER BY w.`id`, r.`position`;

-- Every account gets all resources; root and admin may use every method, a user may only read.
INSERT INTO `account_endpoint` (`account_id`, `resource`)
SELECT a.`id`, e.`resource` FROM `account` a
CROSS JOIN (
    SELECT 'warehouses' AS `resource` UNION ALL SELECT 'locations' UNION ALL SELECT 'transfers'
    UNION ALL SELECT 'items' UNION ALL SELECT 'item_lines' UNION ALL SELECT 'item_groups'
    UNION ALL SELECT 'item_types' UNION ALL SELECT 'suppliers' UNION ALL SELECT 'orders'
    UNION ALL SELECT 'clients' UNION ALL SELECT 'shipments' UNION ALL SELECT 'inventories') e;

INSERT INTO `account_permission` (`account_id`, `resource`, `method`, `allowed`)
SELECT e.`account_id`, e.`resource`, m.`method`, a.`role` <> 'user' OR m.`method` = 'get'
FROM `account_endpoint` e
JOIN `account` a ON a.`id` = e.`account_id`
CROSS JOIN (SELECT 'get' AS `method` UNION ALL SELECT 'post' UNION ALL SELECT 'put'
            UNION ALL SELECT 'delete') m;

UPDATE `schema_version` SET `version` = 2;

COMMIT;

DROP TABLE IF EXISTS `user_permission`, `user_endpoint`, `user`;
