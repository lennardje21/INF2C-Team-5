using CargoHub.Api.Models;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CargoHub.Api.OpenApi;

/// <summary>
/// Marks the fields the server fills in itself as read-only, so the docs page shows them
/// in responses but leaves them out of request examples. Values sent for them are ignored.
/// </summary>
public sealed class ServerSetFieldsSchemaTransformer : IOpenApiSchemaTransformer
{
    // Keep in sync with the services: these are the fields they overwrite on create/update.
    private static readonly Dictionary<Type, string[]> ServerSetFields = new()
    {
        [typeof(BaseModel)] = ["id", "createdAt", "updatedAt"],
        [typeof(Order)] = ["reference"],
        [typeof(Shipment)] = ["reference"],
        [typeof(Transfer)] = ["reference"],
        [typeof(OrderItem)] = ["id", "orderId"],
        [typeof(ShipmentItem)] = ["id", "shipmentId"],
        [typeof(TransferItem)] = ["id", "transferId"]
    };

    public Task TransformAsync(
        OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (schema.Properties is null)
            return Task.CompletedTask;

        var type = context.JsonTypeInfo.Type;
        foreach (var (owner, names) in ServerSetFields.Where(f => f.Key.IsAssignableFrom(type)))
        {
            foreach (var name in names)
            {
                if (schema.Properties.TryGetValue(name, out var property) && property is OpenApiSchema field)
                    field.ReadOnly = true;
            }
        }
        return Task.CompletedTask;
    }
}
