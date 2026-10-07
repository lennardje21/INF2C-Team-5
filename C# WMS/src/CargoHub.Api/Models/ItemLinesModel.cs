namespace CargoHub.Api.Models;

public class ItemLinesModel
{
    public int Id { get; set; }

    public string name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
