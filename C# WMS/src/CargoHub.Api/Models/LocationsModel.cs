namespace CargoHub.Api.Models;

public class LocationsModel
{
    public int Id { get; set; }

    public int WarehouseId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
