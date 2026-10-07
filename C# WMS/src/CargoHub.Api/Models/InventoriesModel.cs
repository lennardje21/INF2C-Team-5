namespace Cargohub.Api.Models;

public class InventoriesModel : BaseModel
{
    public int ItemId {get; set;}
    public int LocationId {get; set;}
    public int QuantityOnHand {get; set;}
    public int QuantityExpected {get; set;}
    public int QuantityOrdered {get; set;}
    public int QuantityAllocated {get; set;}
}