namespace CargoHub.Api.Models;

public class ShipmentItem
{
    public int Id { get; set; }

    public int ShipmentId { get; set; }

    public int ItemId { get; set; }

    public int Amount { get; set; }
}
