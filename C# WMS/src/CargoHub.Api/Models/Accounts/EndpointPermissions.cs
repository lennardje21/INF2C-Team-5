namespace CargoHub.Api.Models;

// The methods an account may use on one resource; see Account.EndpointAccess.
public sealed class EndpointPermissions
{
    public bool Get { get; set; }

    public bool Post { get; set; }

    public bool Put { get; set; }

    public bool Delete { get; set; }
}
