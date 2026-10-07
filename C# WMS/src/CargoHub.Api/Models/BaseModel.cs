namespace CargoHub.Api.Models

public abstract class BaseModel
{
    public int Id {get; set;}
    public DateTime? CreatedAt {get; set;}
    public DateTime? CreateAt {get; set;}
    public DateTime? UpdatedAt {get; set;}
}