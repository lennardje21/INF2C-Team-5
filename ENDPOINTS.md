# API Endpoints

All endpoints are prefixed with `/api/v1`.
`{id}` is a variable (no fixed value).

## Warehouses (6 routes)

| Method |             Path              |           description            |
|--------|-------------------------------|----------------------------------|
|  GET   | /warehouses                   | List warehouses                  |
|  GET   | /warehouses/{id}              | Get a warehouse                  |
|  GET   | /warehouses/{id}/locations    | List locations in a warehouse    |
|  POST  | /warehouses                   | Create a warehouse               |
|  PUT   | /warehouses/{id}              | Update a warehouse               |
| DELETE | /warehouses/{id}              | Delete a warehouse               |

## Locations (5 routes)

| Method |             Path              |           description            |
|--------|-------------------------------|----------------------------------|
|  GET   | /locations                    | List locations                   |
|  GET   | /locations/{id}               | Get a location                   |
|  POST  | /locations                    | Create a location                |
|  PUT   | /locations/{id}               | Update a location                |
| DELETE | /locations/{id}               | Delete a location                |

## Transfers (7 routes)

| Method |             Path              |           description            |
|--------|-------------------------------|----------------------------------|
|  GET   | /transfers                    | List transfers                   |
|  GET   | /transfers/{id}               | Get a transfer                   |
|  GET   | /transfers/{id}/items         | List items in a transfer         |
|  POST  | /transfers                    | Create a transfer                |
|  PUT   | /transfers/{id}               | Update a transfer                |
|  PUT   | /transfers/{id}/commit        | Process transfer, move stock     |
| DELETE | /transfers/{id}               | Delete a transfer                |

## Items (7 routes)

| Method |             Path              |           description            |
|--------|-------------------------------|----------------------------------|
|  GET   | /items                        | List items                       |
|  GET   | /items/{id}                   | Get an item                      |
|  GET   | /items/{id}/inventory         | List inventory per location      |
|  GET   | /items/{id}/inventory/totals  | Get total inventory              |
|  POST  | /items                        | Create an item                   |
|  PUT   | /items/{id}                   | Update an item                   |
| DELETE | /items/{id}                   | Delete an item                   |

## Item Lines (6 routes)

| Method |             Path              |           description            |
|--------|-------------------------------|----------------------------------|
|  GET   | /item_lines                   | List item lines                  |
|  GET   | /item_lines/{id}              | Get an item line                 |
|  GET   | /item_lines/{id}/items        | List items in a line             |
|  POST  | /item_lines                   | Create an item line              |
|  PUT   | /item_lines/{id}              | Update an item line              |
| DELETE | /item_lines/{id}              | Delete an item line              |

## Item Groups (6 routes)

| Method |             Path              |           description            |
|--------|-------------------------------|----------------------------------|
|  GET   | /item_groups                  | List item groups                 |
|  GET   | /item_groups/{id}             | Get an item group                |
|  GET   | /item_groups/{id}/items       | List items in a group            |
|  POST  | /item_groups                  | Create an item group             |
|  PUT   | /item_groups/{id}             | Update an item group             |
| DELETE | /item_groups/{id}             | Delete an item group             |

## Item Types (6 routes)

| Method |             Path              |           description            |
|--------|-------------------------------|----------------------------------|
|  GET   | /item_types                   | List item types                  |
|  GET   | /item_types/{id}              | Get an item type                 |
|  GET   | /item_types/{id}/items        | List items of a type             |
|  POST  | /item_types                   | Create an item type              |
|  PUT   | /item_types/{id}              | Update an item type              |
| DELETE | /item_types/{id}              | Delete an item type              |

## Inventories (4 routes)

| Method |             Path              |           description            |
|--------|-------------------------------|----------------------------------|
|  GET   | /inventories                  | List inventory records           |
|  POST  | /inventories                  | Create an inventory record       |
|  PUT   | /inventories/{id}             | Not implemented (404)            |
| DELETE | /inventories/{id}             | Not implemented (404)            |

## Suppliers (6 routes)

| Method |             Path              |           description            |
|--------|-------------------------------|----------------------------------|
|  GET   | /suppliers                    | List suppliers                   |
|  GET   | /suppliers/{id}               | Get a supplier                   |
|  GET   | /suppliers/{id}/items         | List items from a supplier       |
|  POST  | /suppliers                    | Create a supplier                |
|  PUT   | /suppliers/{id}               | Update a supplier                |
| DELETE | /suppliers/{id}               | Delete a supplier                |

## Orders (7 routes)

| Method |             Path              |           description            |
|--------|-------------------------------|----------------------------------|
|  GET   | /orders                       | List orders                      |
|  GET   | /orders/{id}                  | Get an order                     |
|  GET   | /orders/{id}/items            | List items in an order           |
|  POST  | /orders                       | Create an order                  |
|  PUT   | /orders/{id}                  | Update an order                  |
|  PUT   | /orders/{id}/items            | Update items in an order         |
| DELETE | /orders/{id}                  | Delete an order                  |

## Clients (6 routes)

| Method |             Path              |           description            |
|--------|-------------------------------|----------------------------------|
|  GET   | /clients                      | List clients                     |
|  GET   | /clients/{id}                 | Get a client                     |
|  GET   | /clients/{id}/orders          | List orders from a client        |
|  POST  | /clients                      | Create a client                  |
|  PUT   | /clients/{id}                 | Update a client                  |
| DELETE | /clients/{id}                 | Delete a client                  |

## Shipments (9 routes)

| Method |             Path              |           description            |
|--------|-------------------------------|----------------------------------|
|  GET   | /shipments                    | List shipments                   |
|  GET   | /shipments/{id}               | Get a shipment                   |
|  GET   | /shipments/{id}/orders        | List orders in a shipment        |
|  GET   | /shipments/{id}/items         | List items in a shipment         |
|  POST  | /shipments                    | Create a shipment                |
|  PUT   | /shipments/{id}               | Update a shipment                |
|  PUT   | /shipments/{id}/orders        | Update orders in a shipment      |
|  PUT   | /shipments/{id}/items         | Update items in a shipment       |
| DELETE | /shipments/{id}               | Delete a shipment                |
