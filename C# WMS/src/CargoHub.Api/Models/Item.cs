namespace CargoHub.Api.Models;

public class Item : BaseModel
{
    public string Code { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Barcode { get; set; } = string.Empty;

    public string ModelNumber { get; set; } = string.Empty;

    public int CommodityCode { get; set; }

    public decimal UnitWeight { get; set; }

    public int ItemLineId { get; set; }

    public int ItemGroupId { get; set; }

    public int ItemTypeId { get; set; }

    public int MinPurchaseQuantity { get; set; }

    public int CaseSize { get; set; }

    public string PackagingType { get; set; } = string.Empty;

    public int OrderMultiple { get; set; }

    public int SupplierId { get; set; }

    public string SupplierSku { get; set; } = string.Empty;
}
