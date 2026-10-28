using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using CargoHub.Api.Models;
using MySqlConnector;

namespace CargoHub.Api.Data;

/// <summary>
/// Persists scalar fields of the existing models. Child collections are loaded
/// separately, and business operations share an explicit connection/transaction.
/// </summary>
public sealed class MySqlRepository<T>(MySqlConnectionFactory factory) where T : class, new()
{
    private static readonly Dictionary<Type, string> Tables = new()
    {
        [typeof(Warehouse)] = "warehouse", [typeof(Client)] = "client",
        [typeof(Supplier)] = "supplier", [typeof(ItemLine)] = "item_line",
        [typeof(ItemGroup)] = "item_group", [typeof(ItemType)] = "item_type",
        [typeof(Location)] = "location", [typeof(Item)] = "item",
        [typeof(Inventory)] = "inventory", [typeof(Order)] = "order",
        [typeof(OrderItem)] = "order_item", [typeof(Shipment)] = "shipment",
        [typeof(ShipmentItem)] = "shipment_item", [typeof(Transfer)] = "transfer",
        [typeof(TransferItem)] = "transfer_item"
    };

    private static readonly string Table = Tables.TryGetValue(typeof(T), out var table)
        ? table : throw new NotSupportedException($"No table mapping for {typeof(T).Name}.");
    private static readonly PropertyInfo[] Properties = typeof(T).GetProperties()
        .Where(p => p.PropertyType == typeof(string) || p.PropertyType.IsValueType).ToArray();
    private static readonly Dictionary<string, PropertyInfo> Columns = Properties.ToDictionary(
        p => p.Name == nameof(Item.MinPurchaseQuantity) ? "min_purchase_qty"
            : Regex.Replace(p.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant(),
        p => p);
    private static readonly string[] Keys = typeof(T) == typeof(Inventory)
        ? ["item_id", "location_id"] : ["id"];

    public async Task<IReadOnlyList<T>> GetAsync(
        IReadOnlyDictionary<string, object?>? filters = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        return await GetAsync(connection, null, filters, cancellationToken);
    }

    public async Task<IReadOnlyList<T>> GetAsync(MySqlConnection connection,
        MySqlTransaction? transaction, IReadOnlyDictionary<string, object?>? filters = null,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var conditions = new List<string>();
        if (filters is not null)
        {
            foreach (var (column, value) in filters)
            {
                if (!Columns.ContainsKey(column))
                    throw new ArgumentException($"Unknown column: {column}", nameof(filters));
                var parameter = $"@p{conditions.Count}";
                conditions.Add($"`{column}` <=> {parameter}");
                command.Parameters.AddWithValue(parameter, ToDatabase(value));
            }
        }
        command.CommandText = $"SELECT {string.Join(", ", Columns.Keys.Select(c => $"`{c}`"))} " +
            $"FROM `{Table}`" + (conditions.Count == 0 ? "" : " WHERE " + string.Join(" AND ", conditions)) +
            $" ORDER BY {string.Join(", ", Keys.Select(k => $"`{k}`"))}";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<T>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var model = new T();
            foreach (var (column, property) in Columns)
            {
                var value = reader[column];
                if (value is DBNull)
                    property.SetValue(model, null);
                else if (property.PropertyType == typeof(DateTime))
                    property.SetValue(model, DateTime.SpecifyKind((DateTime)value, DateTimeKind.Utc));
                else
                    property.SetValue(model, Convert.ChangeType(value,
                        Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType,
                        CultureInfo.InvariantCulture));
            }
            result.Add(model);
        }
        return result;
    }

    public async Task InsertAsync(T model, MySqlConnection connection,
        MySqlTransaction transaction, CancellationToken cancellationToken = default)
    {
        var generatedId = Columns.TryGetValue("id", out var id) && (int)id.GetValue(model)! == 0;
        var fields = Columns.Where(c => !generatedId || c.Key != "id").ToArray();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"INSERT INTO `{Table}` (" +
            string.Join(", ", fields.Select(c => $"`{c.Key}`")) + ") VALUES (" +
            string.Join(", ", fields.Select((_, i) => $"@p{i}")) + ")";
        for (var i = 0; i < fields.Length; i++)
            command.Parameters.AddWithValue($"@p{i}", ToDatabase(fields[i].Value.GetValue(model)));
        await command.ExecuteNonQueryAsync(cancellationToken);
        NormalizeModelDates(model);
        if (generatedId)
            id!.SetValue(model, checked((int)command.LastInsertedId));
    }

    public async Task UpdateAsync(T model, MySqlConnection connection,
        MySqlTransaction transaction, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var fields = Columns.Where(c => !Keys.Contains(c.Key)).ToArray();
        command.CommandText = $"UPDATE `{Table}` SET " +
            string.Join(", ", fields.Select((c, i) => $"`{c.Key}` = @p{i}")) +
            " WHERE " + string.Join(" AND ", Keys.Select((k, i) => $"`{k}` = @key{i}"));
        for (var i = 0; i < fields.Length; i++)
            command.Parameters.AddWithValue($"@p{i}", ToDatabase(fields[i].Value.GetValue(model)));
        AddKeys(command, model);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new KeyNotFoundException($"No {typeof(T).Name} matched the primary key.");
        NormalizeModelDates(model);
    }

    public async Task DeleteAsync(T model, MySqlConnection connection,
        MySqlTransaction transaction, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DELETE FROM `{Table}` WHERE " +
            string.Join(" AND ", Keys.Select((k, i) => $"`{k}` = @key{i}"));
        AddKeys(command, model);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new KeyNotFoundException($"No {typeof(T).Name} matched the primary key.");
    }

    private static void AddKeys(MySqlCommand command, T model)
    {
        for (var i = 0; i < Keys.Length; i++)
            command.Parameters.AddWithValue($"@key{i}", Columns[Keys[i]].GetValue(model));
    }

    // DATETIME(6) stores microseconds; DateTime has 100-nanosecond ticks.
    // Keep returned models identical to the values actually sent to MySQL.
    private static void NormalizeModelDates(T model)
    {
        foreach (var property in Properties.Where(p => p.PropertyType == typeof(DateTime)))
            property.SetValue(model, ToDatabase(property.GetValue(model)));
    }

    private static object ToDatabase(object? value) => value switch
    {
        null => DBNull.Value,
        DateTime date when date.Kind == DateTimeKind.Local => ToDatabase(date.ToUniversalTime()),
        DateTime date when date.Kind == DateTimeKind.Unspecified =>
            throw new ArgumentException("Use an explicitly UTC or local DateTime."),
        DateTime date => new DateTime(date.Ticks - date.Ticks % TimeSpan.TicksPerMicrosecond, DateTimeKind.Utc),
        _ => value
    };
}
