-- Each actual count must equal expected. Foreign keys stay enabled during import.
SELECT 'warehouse' AS table_name, COUNT(*) AS actual, 10 AS expected FROM `warehouse`
UNION ALL
SELECT 'client' AS table_name, COUNT(*) AS actual, 300 AS expected FROM `client`
UNION ALL
SELECT 'supplier' AS table_name, COUNT(*) AS actual, 21 AS expected FROM `supplier`
UNION ALL
SELECT 'item_line' AS table_name, COUNT(*) AS actual, 14 AS expected FROM `item_line`
UNION ALL
SELECT 'item_group' AS table_name, COUNT(*) AS actual, 3 AS expected FROM `item_group`
UNION ALL
SELECT 'item_type' AS table_name, COUNT(*) AS actual, 3 AS expected FROM `item_type`
UNION ALL
SELECT 'location' AS table_name, COUNT(*) AS actual, 400 AS expected FROM `location`
UNION ALL
SELECT 'item' AS table_name, COUNT(*) AS actual, 600 AS expected FROM `item`
UNION ALL
SELECT 'inventory' AS table_name, COUNT(*) AS actual, 4800 AS expected FROM `inventory`
UNION ALL
SELECT 'order' AS table_name, COUNT(*) AS actual, 4854 AS expected FROM `order`
UNION ALL
SELECT 'order_item' AS table_name, COUNT(*) AS actual, 26498 AS expected FROM `order_item`
UNION ALL
SELECT 'shipment' AS table_name, COUNT(*) AS actual, 6132 AS expected FROM `shipment`
UNION ALL
SELECT 'shipment_item' AS table_name, COUNT(*) AS actual, 33660 AS expected FROM `shipment_item`
UNION ALL
SELECT 'transfer' AS table_name, COUNT(*) AS actual, 800 AS expected FROM `transfer`
UNION ALL
SELECT 'transfer_item' AS table_name, COUNT(*) AS actual, 2370 AS expected FROM `transfer_item`
UNION ALL
SELECT 'user' AS table_name, COUNT(*) AS actual, 6 AS expected FROM `user`
UNION ALL
SELECT 'user_endpoint' AS table_name, COUNT(*) AS actual, 72 AS expected FROM `user_endpoint`
UNION ALL
SELECT 'user_permission' AS table_name, COUNT(*) AS actual, 288 AS expected FROM `user_permission`;
SELECT `version` FROM `schema_version`;
