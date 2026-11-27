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
        [typeof(TransferItem)] = "transfer_item",
        [typeof(Account)] = "account"
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
    private const string UpdatedAtColumn = "updated_at";
    private static readonly string SelectFrom =
        $"SELECT {string.Join(", ", Columns.Keys.Select(c => $"`{c}`"))} FROM `{Table}`";

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
        command.CommandText = SelectFrom + Where(Conditions(command, filters)) +
            $" ORDER BY {string.Join(", ", Keys.Select(k => $"`{k}`"))}";
        return await ReadAsync(command, cancellationToken);
    }

    /// <summary>
    /// One page of the rows that match the filters and the updated_at range of the request,
    /// plus the total number of matching rows. Rows are ordered by updated_at and then by key,
    /// both in the requested direction, so a page never overlaps the next.
    /// </summary>
    public async Task<PagedResult<T>> GetAsync(PageRequest page,
        IReadOnlyDictionary<string, object?>? filters = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        return await GetAsync(connection, null, page, filters, cancellationToken);
    }

    public async Task<PagedResult<T>> GetAsync(MySqlConnection connection,
        MySqlTransaction? transaction, PageRequest page,
        IReadOnlyDictionary<string, object?>? filters = null,
        CancellationToken cancellationToken = default)
    {
        page.EnsureValid();
        if (!Columns.ContainsKey(UpdatedAtColumn))
            throw new NotSupportedException($"{typeof(T).Name} has no {UpdatedAtColumn} column to page on.");

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var conditions = Conditions(command, filters);
        if (page.FromInclusive is { } from)
        {
            conditions.Add($"`{UpdatedAtColumn}` >= @from");
            command.Parameters.AddWithValue("@from", ToDatabase(from));
        }
        if (page.ToExclusive is { } to)
        {
            conditions.Add($"`{UpdatedAtColumn}` < @to");
            command.Parameters.AddWithValue("@to", ToDatabase(to));
        }
        var where = Where(conditions);

        command.CommandText = $"SELECT COUNT(*) FROM `{Table}`{where}";
        var total = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
        if (page.Offset >= total)
            return new PagedResult<T>([], page.Page, page.Size, total);

        var direction = page.Sort == SortDirection.Asc ? "ASC" : "DESC";
        command.CommandText = SelectFrom + where +
            $" ORDER BY `{UpdatedAtColumn}` {direction}, " +
            string.Join(", ", Keys.Select(k => $"`{k}` {direction}")) +
            " LIMIT @limit OFFSET @offset";
        command.Parameters.AddWithValue("@limit", page.Size);
        command.Parameters.AddWithValue("@offset", page.Offset);
        return new PagedResult<T>(await ReadAsync(command, cancellationToken), page.Page, page.Size, total);
    }

    // A value that is a list of ids matches any of them (IN); anything else must be equal (<=>, so null works).
    private static List<string> Conditions(MySqlCommand command, IReadOnlyDictionary<string, object?>? filters)
    {
        var conditions = new List<string>();
        var parameters = 0;
        foreach (var (column, value) in filters ?? new Dictionary<string, object?>())
        {
            if (!Columns.ContainsKey(column))
                throw new ArgumentException($"Unknown column: {column}", nameof(filters));
            if (value is IEnumerable<int> ids)
            {
                var names = new List<string>();
                foreach (var id in ids)
                {
                    var name = $"@p{parameters++}";
                    names.Add(name);
                    command.Parameters.AddWithValue(name, id);
                }
                conditions.Add(names.Count == 0 ? "FALSE" : $"`{column}` IN ({string.Join(", ", names)})");
                continue;
            }
            var parameter = $"@p{parameters++}";
            conditions.Add($"`{column}` <=> {parameter}");
            command.Parameters.AddWithValue(parameter, ToDatabase(value));
        }
        return conditions;
    }

    private static string Where(List<string> conditions) =>
        conditions.Count == 0 ? "" : " WHERE " + string.Join(" AND ", conditions);

    private static async Task<IReadOnlyList<T>> ReadAsync(MySqlCommand command,
        CancellationToken cancellationToken)
    {
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
