namespace CargoHub.Api.Models;

public class Shipment : BaseModel
{
    public string Reference { get; set; } = string.Empty;

    public int OrderId { get; set; }

    public DateTime ShipmentDate { get; set; }

    public string ShipmentType { get; set; } = string.Empty;

    public string ShipmentStatus { get; set; } = string.Empty;

    public string CarrierName { get; set; } = string.Empty;

    public string ShippingMethod { get; set; } = string.Empty;

    public string PaymentType { get; set; } = string.Empty;

    public List<ShipmentItem> Items { get; set; } = [];
}
