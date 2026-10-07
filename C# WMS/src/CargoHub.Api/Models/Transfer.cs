namespace CargoHub.Api.Models;

public class Transfer : BaseModel
{
    public string Reference { get; set; } = string.Empty;

    public int FromLocationId { get; set; }

    public int ToLocationId { get; set; }

    public string TransferStatus { get; set; } = string.Empty;

    public List<TransferItem> Items { get; set; } = [];
}
