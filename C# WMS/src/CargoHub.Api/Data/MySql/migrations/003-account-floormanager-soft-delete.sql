-- Version 2 -> 3: the floormanager role (between admin and user), and soft delete for
-- accounts: deleted_at, with name and email cleared, so the row stays for anything that
-- refers to it. The unnamed CHECK constraints of version 2 get names, the same as in
-- 01-schema.sql, so new and migrated databases are identical.
--
-- deploy/deploy.sh applies this automatically (after a backup). It needs the mysql client,
-- which stops at the first error. It is safe to run again after failing at any point:
--   1. all table changes are one ALTER TABLE, which MySQL applies completely or not at all,
--      and it is skipped when an earlier attempt already applied it;
--   2. only then does schema_version become 3, as the very last statement.
SET NAMES utf8mb4;
SET time_zone = '+00:00';

DROP PROCEDURE IF EXISTS `migrate_003`;
DELIMITER //
CREATE PROCEDURE `migrate_003`()
BEGIN
    IF (SELECT MAX(`version`) FROM `schema_version`) <> 2 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Migration 003 needs schema_version 2.';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS
                   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'account' AND COLUMN_NAME = 'deleted_at') THEN
        -- Version 2 created its constraints without names; make sure these are the right ones.
        IF NOT EXISTS (SELECT 1 FROM information_schema.CHECK_CONSTRAINTS
                       WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'account_chk_1'
                         AND CHECK_CLAUSE LIKE '%superroot%' AND CHECK_CLAUSE NOT LIKE '%warehouse_id%')
           OR NOT EXISTS (SELECT 1 FROM information_schema.CHECK_CONSTRAINTS
                          WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'account_chk_2'
                            AND CHECK_CLAUSE LIKE '%warehouse_id%') THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Migration 003 did not find the CHECK constraints of version 2.';
        END IF;

        ALTER TABLE `account`
            MODIFY `name` VARCHAR(255) NULL,
            MODIFY `email` VARCHAR(255) NULL,
            ADD COLUMN `deleted_at` DATETIME(6) NULL AFTER `updated_at`,
            DROP CHECK `account_chk_1`,
            DROP CHECK `account_chk_2`,
            ADD CONSTRAINT `account_role`
                CHECK (`role` IN ('superroot', 'root', 'admin', 'floormanager', 'user')),
            ADD CONSTRAINT `account_superroot_warehouse`
                CHECK ((`role` = 'superroot') = (`warehouse_id` IS NULL)),
            ADD CONSTRAINT `account_deleted_cleared`
                CHECK (`deleted_at` IS NULL OR (`name` IS NULL AND `email` IS NULL AND `api_key_hash` IS NULL)),
            ADD CONSTRAINT `account_active_named`
                CHECK (`deleted_at` IS NOT NULL OR (`name` IS NOT NULL AND `email` IS NOT NULL));
    END IF;
END //
DELIMITER ;
CALL `migrate_003`();
DROP PROCEDURE `migrate_003`;

UPDATE `schema_version` SET `version` = 3;
