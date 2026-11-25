-- Version 3 -> 4: blockade detection (FR-7). Adds two tables: blockade_setting (the times an
-- admin or root sets through the API) and blockade_notification (which blockades were mailed).
-- No data is changed or removed. The floormanager role came with migration 003.
--
-- deploy/deploy.sh applies this automatically (after a backup) when it deploys the first
-- image that contains it. It needs the mysql client, which stops at the first error.
--
-- MySQL commits every DDL statement on its own, so a failure can leave a partial result.
-- The order makes that safe to run again (deploy.sh retries every 5 minutes):
--   1. the version is checked first, so a wrong database is never touched;
--   2. the two tables are dropped and created again, so a partial earlier attempt is replaced;
--   3. only then does schema_version become 4, as the very last statement.
SET NAMES utf8mb4;
SET time_zone = '+00:00';

DROP PROCEDURE IF EXISTS `check_004_blockade_detection`;
DELIMITER //
CREATE PROCEDURE `check_004_blockade_detection`()
BEGIN
    IF (SELECT MAX(`version`) FROM `schema_version`) <> 3 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Migration 004 needs schema_version 3.';
    END IF;
END //
DELIMITER ;
CALL `check_004_blockade_detection`();
DROP PROCEDURE `check_004_blockade_detection`;

DROP TABLE IF EXISTS `blockade_notification`, `blockade_setting`;

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

UPDATE `schema_version` SET `version` = 4;
