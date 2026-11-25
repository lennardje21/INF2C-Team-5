-- Version 2 -> 3: blockade detection (FR-7). Adds the floormanager role, which receives the
-- blockade mails of its warehouse, and two tables: blockade_setting (the times an admin or
-- root sets through the API) and blockade_notification (which blockades were mailed).
-- No data is changed or removed.
--
-- deploy/deploy.sh applies this automatically (after a backup) when it deploys the first
-- image that contains it. It needs the mysql client, which stops at the first error.
--
-- MySQL commits every DDL statement on its own, so a failure can leave a partial result.
-- The order makes that safe to run again (deploy.sh retries every 5 minutes):
--   1. the version is checked first, so a wrong database is never touched;
--   2. the two new tables are dropped and created again, so a partial earlier attempt is replaced;
--   3. the CHECK on account.role is replaced in one ALTER TABLE, which is atomic, and only
--      when it does not allow floormanager yet;
--   4. only then does schema_version become 3, as the very last statement.
SET NAMES utf8mb4;
SET time_zone = '+00:00';

DROP PROCEDURE IF EXISTS `check_003_blockade_detection`;
DELIMITER //
CREATE PROCEDURE `check_003_blockade_detection`()
BEGIN
    IF (SELECT MAX(`version`) FROM `schema_version`) <> 2 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Migration 003 needs schema_version 2.';
    END IF;
END //
DELIMITER ;
CALL `check_003_blockade_detection`();
DROP PROCEDURE `check_003_blockade_detection`;

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

-- The CHECK on account.role has a generated name in a database that came from migration
-- 002, so its name is looked up. The role check is the one that names 'admin'.
DROP PROCEDURE IF EXISTS `allow_floormanager_003`;
DELIMITER //
CREATE PROCEDURE `allow_floormanager_003`()
BEGIN
    DECLARE old_check VARCHAR(64) DEFAULT NULL;

    SELECT cc.`CONSTRAINT_NAME` INTO old_check
    FROM information_schema.`CHECK_CONSTRAINTS` cc
    JOIN information_schema.`TABLE_CONSTRAINTS` tc
      ON tc.`CONSTRAINT_SCHEMA` = cc.`CONSTRAINT_SCHEMA` AND tc.`CONSTRAINT_NAME` = cc.`CONSTRAINT_NAME`
    WHERE tc.`CONSTRAINT_SCHEMA` = DATABASE() AND tc.`TABLE_NAME` = 'account'
      AND tc.`CONSTRAINT_TYPE` = 'CHECK'
      AND cc.`CHECK_CLAUSE` LIKE '%admin%' AND cc.`CHECK_CLAUSE` NOT LIKE '%floormanager%'
    LIMIT 1;

    IF old_check IS NOT NULL THEN
        SET @alter_role_check = CONCAT(
            'ALTER TABLE `account` DROP CHECK `', old_check, '`, ',
            'ADD CONSTRAINT `chk_account_role` CHECK (`role` IN (',
            '''superroot'', ''root'', ''admin'', ''floormanager'', ''user''))');
        PREPARE alter_role_check FROM @alter_role_check;
        EXECUTE alter_role_check;
        DEALLOCATE PREPARE alter_role_check;
    END IF;
END //
DELIMITER ;
CALL `allow_floormanager_003`();
DROP PROCEDURE `allow_floormanager_003`;

UPDATE `schema_version` SET `version` = 3;
