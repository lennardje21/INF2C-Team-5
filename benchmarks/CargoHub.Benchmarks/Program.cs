using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CargoHub.Api.Data;
using CargoHub.Api.Models;
using Microsoft.Extensions.Configuration;

var connectionString = Environment.GetEnvironmentVariable("CARGOHUB_BENCH_MYSQL")
    ?? throw new InvalidOperationException("Set CARGOHUB_BENCH_MYSQL to an isolated seeded test database.");
var factory = new MySqlConnectionFactory(new ConfigurationBuilder().AddInMemoryCollection(
    new Dictionary<string, string?> { ["ConnectionStrings:CargoHub"] = connectionString }).Build());
var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
options.Converters.Add(new UtcConverter());
var fixedTimestamp = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);

async Task<object?> Read<T>(Dictionary<string, object?>? filters = null) where T : class, new()
    => await new MySqlRepository<T>(factory).GetAsync(filters);
Dictionary<string, object?> Filter(string key, int value) => new() { [key] = value };

async Task<object?> Run(string name)
{
    switch (name)
    {
        case "item_one": return (await new MySqlRepository<Item>(factory).GetAsync(Filter("id", 600))).Single();
        case "item_missing": return (await new MySqlRepository<Item>(factory).GetAsync(Filter("id", -1))).SingleOrDefault();
        case "item_all": return await Read<Item>();
        case "inventory_all": return await Read<Inventory>();
        case "inventory_by_item": return await Read<Inventory>(Filter("item_id", 600));
        case "order_items": return await Read<OrderItem>(Filter("order_id", 4854));
        case "shipment_items": return await Read<ShipmentItem>(Filter("shipment_id", 6132));
        case "inventory_totals":
            var stock = await new MySqlRepository<Inventory>(factory).GetAsync(Filter("item_id", 600));
            return new { TotalExpected = stock.Sum(x => x.QuantityExpected),
                TotalOrdered = stock.Sum(x => x.QuantityOrdered),
                TotalAllocated = stock.Sum(x => x.QuantityAllocated),
                TotalAvailable = stock.Sum(x => x.QuantityOnHand - x.QuantityAllocated) };
        case "order_with_items":
            var order = (await new MySqlRepository<Order>(factory).GetAsync(Filter("id", 4854))).Single();
            var orderNode = JsonSerializer.SerializeToNode(order, options)!.AsObject();
            var orderItems = await new MySqlRepository<OrderItem>(factory).GetAsync(Filter("order_id", 4854));
            orderNode["items"] = JsonSerializer.SerializeToNode(orderItems.Select(x => new { x.ItemId, x.Amount }), options);
            return orderNode;
        case "shipment_with_items":
            var shipment = (await new MySqlRepository<Shipment>(factory).GetAsync(Filter("id", 6132))).Single();
            var shipmentNode = JsonSerializer.SerializeToNode(shipment, options)!.AsObject();
            var shipmentItems = await new MySqlRepository<ShipmentItem>(factory).GetAsync(Filter("shipment_id", 6132));
            shipmentNode["items"] = JsonSerializer.SerializeToNode(shipmentItems.Select(x => new { x.ItemId, x.Amount }), options);
            return shipmentNode;
        case "transfer_with_items":
            var transfer = (await new MySqlRepository<Transfer>(factory).GetAsync(Filter("id", 800))).Single();
            var transferNode = JsonSerializer.SerializeToNode(transfer, options)!.AsObject();
            var transferItems = await new MySqlRepository<TransferItem>(factory).GetAsync(Filter("transfer_id", 800));
            transferNode["items"] = JsonSerializer.SerializeToNode(transferItems.Select(x => new { x.ItemId, x.Amount }), options);
            return transferNode;
        case "inventory_update":
            await using (var connection = await factory.OpenAsync())
            await using (var transaction = await connection.BeginTransactionAsync())
            {
                var repository = new MySqlRepository<Inventory>(factory);
                var row = (await repository.GetAsync(connection, transaction,
                    new Dictionary<string, object?> { ["item_id"] = 600, ["location_id"] = 121 })).Single();
                row.QuantityOnHand++;
                row.UpdatedAt = fixedTimestamp;
                await repository.UpdateAsync(row, connection, transaction);
                await transaction.CommitAsync();
                return row;
            }
        case "item_update":
            await using (var connection = await factory.OpenAsync())
            await using (var transaction = await connection.BeginTransactionAsync())
            {
                var repository = new MySqlRepository<Item>(factory);
                var row = (await repository.GetAsync(connection, transaction, Filter("id", 600))).Single();
                row.MinPurchaseQuantity++;
                row.UpdatedAt = fixedTimestamp;
                await repository.UpdateAsync(row, connection, transaction);
                await transaction.CommitAsync();
                return row;
            }
        case "group_roundtrip":
            var groups = new MySqlRepository<ItemGroup>(factory);
            var group = new ItemGroup { Id = 900001, Name = "Benchmark 🚚", Description = "O'Brien",
                CreatedAt = fixedTimestamp, UpdatedAt = fixedTimestamp };
            await using (var connection = await factory.OpenAsync())
            {
                await using (var transaction = await connection.BeginTransactionAsync())
                {
                    await groups.InsertAsync(group, connection, transaction);
                    await transaction.CommitAsync();
                }
                await using (var transaction = await connection.BeginTransactionAsync())
                {
                    await groups.DeleteAsync(group, connection, transaction);
                    await transaction.CommitAsync();
                }
            }
            return group;
        case "table_warehouse": return await Read<Warehouse>();
        case "table_client": return await Read<Client>();
        case "table_supplier": return await Read<Supplier>();
        case "table_item_line": return await Read<ItemLine>();
        case "table_item_group": return await Read<ItemGroup>();
        case "table_item_type": return await Read<ItemType>();
        case "table_location": return await Read<Location>();
        case "table_item": return await Read<Item>();
        case "table_inventory": return await Read<Inventory>();
        case "table_order": return await Read<Order>();
        case "table_order_item": return await Read<OrderItem>();
        case "table_shipment": return await Read<Shipment>();
        case "table_shipment_item": return await Read<ShipmentItem>();
        case "table_transfer": return await Read<Transfer>();
        case "table_transfer_item": return await Read<TransferItem>();
        default: throw new ArgumentException($"Unknown benchmark: {name}");
    }
}

JsonNode? Canonical(object? result, bool removeCollections)
{
    var node = JsonSerializer.SerializeToNode(result, options);
    void Visit(JsonNode? current)
    {
        if (current is JsonObject obj)
        {
            if (obj.ContainsKey("min_purchase_quantity"))
            {
                var value = obj["min_purchase_quantity"];
                obj.Remove("min_purchase_quantity");
                obj["min_purchase_qty"] = value;
            }
            if (removeCollections) obj.Remove("items");
            foreach (var value in obj.ToList()) Visit(value.Value);
        }
        else if (current is JsonArray array)
            foreach (var value in array) Visit(value);
    }
    Visit(node);
    return node;
}

string? line;
while ((line = Console.ReadLine()) is not null)
{
    try
    {
        using var request = JsonDocument.Parse(line);
        var name = request.RootElement.GetProperty("name").GetString()!;
        var count = request.RootElement.GetProperty("count").GetInt32();
        var timings = new List<double>();
        object? result = null;
        for (var i = 0; i < count; i++)
        {
            var watch = Stopwatch.StartNew();
            result = await Run(name);
            watch.Stop();
            timings.Add(watch.Elapsed.TotalMilliseconds);
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            timings_ms = timings,
            result = Canonical(result, name.StartsWith("table_", StringComparison.Ordinal))
        }));
    }
    catch (Exception error)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { error = error.ToString() }));
    }
}

sealed class UtcConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        => reader.GetDateTime();
    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToUniversalTime().ToString(
            "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture));
}
