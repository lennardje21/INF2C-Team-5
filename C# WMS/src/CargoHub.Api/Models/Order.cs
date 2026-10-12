namespace CargoHub.Api.Models;

public class Order : BaseModel
{
    public int ClientId { get; set; }

    public DateTime OrderDate { get; set; }

    public DateTime RequestDate { get; set; }

    public string Reference { get; set; } = string.Empty;

    public string CustomerPoNumber { get; set; } = string.Empty;

    public string OrderStatus { get; set; } = string.Empty;

    public string? ShippingNotes { get; set; }

    public int WarehouseId { get; set; }

    public int ShipToClientId { get; set; }

    public int BillToClientId { get; set; }

    public List<OrderItem> Items { get; set; } = [];
}
