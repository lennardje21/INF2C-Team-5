namespace Cargohub.Api.Models;

public class ItemTypesModel : BaseModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
