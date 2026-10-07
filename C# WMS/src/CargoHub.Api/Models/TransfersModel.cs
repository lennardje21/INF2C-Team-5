namespace CargoHub.Api.Models;

public class TransfersModel
{
    public int Id { get; set; }

    public string Reference { get; set; } = string.Empty;

    public int FromLocationId { get; set; }

    public int ToLocationId { get; set; }

    public string TransferStatus { get; set; } = string.Empty;

    public List<TransferItemsModel> Items { get; set; } = [];

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
