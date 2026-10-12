namespace CargoHub.Api.Models;

// No BaseModel: the inventory table has no id, its key is (ItemId, LocationId).
public class Inventory
{
    public int ItemId { get; set; }

    public int LocationId { get; set; }

    public int QuantityOnHand { get; set; }

    public int QuantityExpected { get; set; }

    public int QuantityOrdered { get; set; }

    public int QuantityAllocated { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
