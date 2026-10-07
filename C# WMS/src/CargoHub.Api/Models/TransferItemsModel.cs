namespace CargoHub.Api.Models;

public class TransferItemsModel
{
    public int Id { get; set; }

    public int TransferId { get; set; }

    public int ItemId { get; set; }

    public int Amount { get; set; }
}