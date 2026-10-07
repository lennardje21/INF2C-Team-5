namespace CargoHub.Api.Models;

public class Supplier : BaseModel
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string ZipCode { get; set; } = string.Empty;

    public string Province { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    public string ContactName { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public string Reference { get; set; } = string.Empty;
}
