using System.Net;
using System.Net.Http.Json;
using CargoHub.Api.Authentication;
using CargoHub.Api.Models;
using CargoHub.Tests.Fixtures;

namespace CargoHub.Tests.Controllers;

// The account endpoints follow the role hierarchy (superroot > root > admin > user) instead
// of endpoint permissions. Every test creates its own accounts below the fixture's superroot,
// so the tests do not depend on each other or on the seeded accounts.
[Collection("Api")]
public sealed class AccountsControllerTests(ApiFixture fixture) : IDisposable
{
    private const string Url = "/api/v2/accounts";
    private const int Warehouse = 1;
    private const int OtherWarehouse = 2;

    private readonly HttpClient _superroot = fixture.Client;
    private readonly List<HttpClient> _clients = [];

    public void Dispose()
    {
        foreach (var client in _clients)
            client.Dispose();
    }

    // An account created through the API, with a client that sends its API key.
    private sealed record Member(CreatedAccount Account, HttpClient Client)
    {
        public int Id => Account.Id;
    }

    // A root, admin and user in one warehouse; each created by the one above it.
    private sealed record Team(Member Root, Member Admin, Member User)
    {
        public Member this[string role] => role switch
        {
            "root" => Root,
            "admin" => Admin,
            "user" => User,
            _ => throw new ArgumentOutOfRangeException(nameof(role))
        };
    }

    private HttpClient ClientWithKey(string apiKey)
    {
        var client = fixture.CreateUnauthenticatedClient();
        client.DefaultRequestHeaders.Add(ApiKeyMiddleware.HeaderName, apiKey);
        _clients.Add(client);
        return client;
    }

    // A dictionary so a test can change or add single fields.
    private static Dictionary<string, object?> NewAccount(string role, int? warehouseId = null) => new()
    {
        ["role"] = role,
        ["name"] = $"Test {role}",
        ["email"] = $"test-{Guid.NewGuid():N}@cargohub.nl",
        ["warehouseId"] = warehouseId,
        ["app"] = "integration-tests"
    };

    // The fields of an existing account, to send back with PUT.
    private static Dictionary<string, object?> Fields(Account account) => new()
    {
        ["role"] = account.Role,
        ["name"] = account.Name,
        ["email"] = account.Email,
        ["warehouseId"] = account.WarehouseId,
        ["app"] = account.App
    };

    // Read access to a single resource.
    private static Dictionary<string, object> ReadAccess(string resource) => new()
    {
        [resource] = new { get = true }
    };

    private async Task<Member> CreateAsync(HttpClient caller, Dictionary<string, object?> account)
    {
        var response = await caller.PostAsJsonAsync(Url, account);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<CreatedAccount>())!;
        return new Member(created, ClientWithKey(created.ApiKey));
    }

    private async Task<Team> CreateTeamAsync(int warehouseId)
    {
        var root = await CreateAsync(_superroot, NewAccount("root", warehouseId));
        var admin = await CreateAsync(root.Client, NewAccount("admin"));
        var user = await CreateAsync(admin.Client, NewAccount("user"));
        return new Team(root, admin, user);
    }

    private static async Task<List<int>> GetIdsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<Account>>(Url))!.Select(a => a.Id).ToList();

    // Creating accounts

    [Fact]
    public async Task CreateAccount_ReturnsAWorkingKeyOnlyOnce()
    {
        var response = await _superroot.PostAsJsonAsync(Url, NewAccount("root", Warehouse));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<CreatedAccount>())!;
        Assert.Equal($"{Url}/{created.Id}", response.Headers.Location!.AbsolutePath);
        Assert.Equal(64, created.ApiKey.Length);

        var me = await ClientWithKey(created.ApiKey).GetFromJsonAsync<Account>($"{Url}/me");
        Assert.Equal(created.Id, me!.Id);

        // Neither the key nor its hash is ever returned again.
        var json = await _superroot.GetStringAsync($"{Url}/{created.Id}");
        Assert.DoesNotContain("apiKey", json);
        Assert.DoesNotContain("hash", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAccount_ByAdmin_DefaultsToTheAdminsWarehouse()
    {
        var team = await CreateTeamAsync(OtherWarehouse);

        Assert.Equal(OtherWarehouse, team.Admin.Account.WarehouseId);
        Assert.Equal(OtherWarehouse, team.User.Account.WarehouseId);
    }

    [Fact]
    public async Task CreateAccount_DefaultAccessFollowsTheRole()
    {
        var team = await CreateTeamAsync(Warehouse);

        var admin = team.Admin.Account.EndpointAccess!["orders"];
        Assert.True(admin.Get && admin.Post && admin.Put && admin.Delete);

        var user = team.User.Account.EndpointAccess!["orders"];
        Assert.True(user.Get);
        Assert.False(user.Post || user.Put || user.Delete);
    }

    // Role restrictions

    [Theory]
    [InlineData("root", "root")]
    [InlineData("root", "superroot")]
    [InlineData("admin", "admin")]
    [InlineData("admin", "root")]
    [InlineData("user", "user")]
    public async Task CreateAccount_WithOwnOrHigherRole_ReturnsForbidden(string callerRole, string role)
    {
        var team = await CreateTeamAsync(Warehouse);

        var response = await team[callerRole].Client.PostAsJsonAsync(Url, NewAccount(role));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateAccount_Superroot_ReturnsForbiddenEvenForTheSuperroot()
    {
        var response = await _superroot.PostAsJsonAsync(Url, NewAccount("superroot"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("admin", "user", "admin")]
    [InlineData("root", "admin", "root")]
    public async Task UpdateAccount_PromotingToOwnRoleOrHigher_ReturnsForbidden(
        string callerRole, string targetRole, string newRole)
    {
        var team = await CreateTeamAsync(Warehouse);
        var fields = Fields(team[targetRole].Account);
        fields["role"] = newRole;

        var response = await team[callerRole].Client.PutAsJsonAsync($"{Url}/{team[targetRole].Id}", fields);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task LowerRoles_CannotSeeOrManageHigherRoles()
    {
        var team = await CreateTeamAsync(Warehouse);

        Assert.Equal(HttpStatusCode.NotFound,
            (await team.User.Client.GetAsync($"{Url}/{team.Admin.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await team.User.Client.PostAsync($"{Url}/{team.Admin.Id}/api_key", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await team.Admin.Client.PutAsJsonAsync($"{Url}/{team.Root.Id}", Fields(team.Root.Account))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await team.Admin.Client.DeleteAsync($"{Url}/{team.Root.Id}")).StatusCode);
    }

    [Fact]
    public async Task GetAccounts_ReturnsTheOwnAccountAndTheAccountsBelowIt()
    {
        var team = await CreateTeamAsync(Warehouse);

        var root = await GetIdsAsync(team.Root.Client);
        Assert.Contains(team.Root.Id, root);
        Assert.Contains(team.Admin.Id, root);
        Assert.Contains(team.User.Id, root);

        var admin = await GetIdsAsync(team.Admin.Client);
        Assert.DoesNotContain(team.Root.Id, admin);
        Assert.Contains(team.Admin.Id, admin);
        Assert.Contains(team.User.Id, admin);

        Assert.Equal([team.User.Id], await GetIdsAsync(team.User.Client));
    }

    // Warehouse restrictions

    [Theory]
    [InlineData("root")]
    [InlineData("admin")]
    public async Task CreateAccount_InOtherWarehouse_ReturnsForbidden(string callerRole)
    {
        var team = await CreateTeamAsync(Warehouse);

        var response = await team[callerRole].Client.PostAsJsonAsync(Url, NewAccount("user", OtherWarehouse));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AccountsInOtherWarehouse_AreNotFound()
    {
        var team = await CreateTeamAsync(Warehouse);
        var other = await CreateTeamAsync(OtherWarehouse);
        var url = $"{Url}/{other.User.Id}";

        Assert.Equal(HttpStatusCode.NotFound, (await team.Root.Client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await team.Root.Client.PutAsJsonAsync(url, Fields(other.User.Account))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await team.Root.Client.DeleteAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await team.Root.Client.PostAsync($"{url}/api_key", null)).StatusCode);

        var visible = await GetIdsAsync(team.Root.Client);
        Assert.DoesNotContain(other.Root.Id, visible);
        Assert.DoesNotContain(other.Admin.Id, visible);
        Assert.DoesNotContain(other.User.Id, visible);
    }

    [Fact]
    public async Task UpdateAccount_ByAdmin_CannotMoveAnAccountToOtherWarehouse()
    {
        var team = await CreateTeamAsync(Warehouse);
        var fields = Fields(team.User.Account);
        fields["warehouseId"] = OtherWarehouse;

        var response = await team.Admin.Client.PutAsJsonAsync($"{Url}/{team.User.Id}", fields);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateAccount_BySuperroot_MovesAnAccountToOtherWarehouse()
    {
        var team = await CreateTeamAsync(Warehouse);
        var other = await CreateTeamAsync(OtherWarehouse);
        var fields = Fields(team.User.Account);
        fields["warehouseId"] = OtherWarehouse;

        var response = await _superroot.PutAsJsonAsync($"{Url}/{team.User.Id}", fields);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await team.Admin.Client.GetAsync($"{Url}/{team.User.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await other.Admin.Client.GetAsync($"{Url}/{team.User.Id}")).StatusCode);
    }

    // Managing your own account

    [Theory]
    [InlineData("root")]
    [InlineData("admin")]
    [InlineData("user")]
    public async Task OwnAccount_CannotBeUpdatedOrDeleted(string role)
    {
        var team = await CreateTeamAsync(Warehouse);
        var member = team[role];
        var fields = Fields(member.Account);
        fields["name"] = "Changed Name";

        Assert.Equal(HttpStatusCode.Forbidden,
            (await member.Client.PutAsJsonAsync($"{Url}/{member.Id}", fields)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await member.Client.DeleteAsync($"{Url}/{member.Id}")).StatusCode);
        Assert.Equal(member.Account.Name, (await member.Client.GetFromJsonAsync<Account>($"{Url}/me"))!.Name);
    }

    [Fact]
    public async Task Superroot_CannotBeDeleted()
    {
        var team = await CreateTeamAsync(Warehouse);
        var superroot = (await _superroot.GetFromJsonAsync<Account>($"{Url}/me"))!;

        Assert.Equal(HttpStatusCode.Forbidden, (await _superroot.DeleteAsync($"{Url}/{superroot.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await team.Root.Client.DeleteAsync($"{Url}/{superroot.Id}")).StatusCode);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("admin")]
    [InlineData("user")]
    public async Task OwnApiKey_CanBeReplaced(string role)
    {
        var team = await CreateTeamAsync(Warehouse);
        var member = team[role];

        var response = await member.Client.PostAsync($"{Url}/{member.Id}/api_key", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var issued = (await response.Content.ReadFromJsonAsync<IssuedApiKey>())!;
        Assert.Equal(HttpStatusCode.Unauthorized, (await member.Client.GetAsync($"{Url}/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ClientWithKey(issued.ApiKey).GetAsync($"{Url}/me")).StatusCode);
    }

    // Permission escalation

    [Fact]
    public async Task CreateAccount_WithAccessTheCallerDoesNotHave_ReturnsForbidden()
    {
        var team = await CreateTeamAsync(Warehouse);
        var limited = NewAccount("admin");
        limited["endpointAccess"] = ReadAccess("item_types");
        var admin = await CreateAsync(team.Root.Client, limited);

        var user = NewAccount("user");
        user["endpointAccess"] = ReadAccess("item_groups");
        var response = await admin.Client.PostAsJsonAsync(Url, user);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateAccount_DefaultAccess_IsLimitedToTheCallersAccess()
    {
        var team = await CreateTeamAsync(Warehouse);
        var limited = NewAccount("admin");
        limited["endpointAccess"] = ReadAccess("item_types");
        var admin = await CreateAsync(team.Root.Client, limited);

        var user = await CreateAsync(admin.Client, NewAccount("user"));

        Assert.True(user.Account.EndpointAccess!["item_types"].Get);
        Assert.False(user.Account.EndpointAccess["item_groups"].Get);
        Assert.Equal(HttpStatusCode.OK, (await user.Client.GetAsync("/api/v2/item_types")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.Client.GetAsync("/api/v2/item_groups")).StatusCode);
    }

    [Fact]
    public async Task UpdateAccount_GrantingAccessTheCallerDoesNotHave_ReturnsForbidden()
    {
        var team = await CreateTeamAsync(Warehouse);
        var limited = NewAccount("admin");
        limited["endpointAccess"] = ReadAccess("item_types");
        var admin = await CreateAsync(team.Root.Client, limited);
        var user = await CreateAsync(admin.Client, NewAccount("user"));
        var fields = Fields(user.Account);
        fields["endpointAccess"] = ReadAccess("item_groups");

        var response = await admin.Client.PutAsJsonAsync($"{Url}/{user.Id}", fields);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.Client.GetAsync("/api/v2/item_groups")).StatusCode);
    }

    // Changes apply at once, on every API container

    [Fact]
    public async Task KeyChangedInTheDatabase_StopsWorkingAtOnce()
    {
        var team = await CreateTeamAsync(Warehouse);
        Assert.Equal(HttpStatusCode.OK, (await team.User.Client.GetAsync($"{Url}/me")).StatusCode);

        // Like scripts/set-api-key.sh, or a change made through the other API container.
        await fixture.ExecuteAsync("UPDATE `account` SET `api_key_hash` = NULL WHERE `id` = @id",
            ("@id", team.User.Id));

        Assert.Equal(HttpStatusCode.Unauthorized, (await team.User.Client.GetAsync($"{Url}/me")).StatusCode);
    }

    [Fact]
    public async Task DeletedAccount_KeyStopsWorkingAtOnce()
    {
        var team = await CreateTeamAsync(Warehouse);

        Assert.Equal(HttpStatusCode.NoContent, (await team.Admin.Client.DeleteAsync($"{Url}/{team.User.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await team.User.Client.GetAsync($"{Url}/me")).StatusCode);
    }

    [Fact]
    public async Task ChangedAccess_AppliesAtOnce()
    {
        var team = await CreateTeamAsync(Warehouse);
        var fields = Fields(team.User.Account);
        fields["endpointAccess"] = ReadAccess("item_groups");

        Assert.Equal(HttpStatusCode.NoContent,
            (await team.Admin.Client.PutAsJsonAsync($"{Url}/{team.User.Id}", fields)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await team.User.Client.GetAsync("/api/v2/item_groups")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await team.User.Client.GetAsync("/api/v2/item_types")).StatusCode);
    }

    // Validation

    [Theory]
    [InlineData("role", "boss")]
    [InlineData("name", "")]
    [InlineData("email", "not-an-email")]
    [InlineData("app", "")]
    [InlineData("warehouseId", int.MaxValue)]
    public async Task CreateAccount_InvalidField_ReturnsBadRequest(string field, object value)
    {
        var account = NewAccount("root", Warehouse);
        account[field] = value;

        var response = await _superroot.PostAsJsonAsync(Url, account);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateAccount_UnknownResource_ReturnsBadRequest()
    {
        var account = NewAccount("root", Warehouse);
        account["endpointAccess"] = ReadAccess("unknown");

        var response = await _superroot.PostAsJsonAsync(Url, account);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateAccount_ExistingEmail_ReturnsBadRequest()
    {
        var first = NewAccount("root", Warehouse);
        await CreateAsync(_superroot, first);
        var second = NewAccount("root", Warehouse);
        second["email"] = ((string)first["email"]!).ToUpperInvariant();

        var response = await _superroot.PostAsJsonAsync(Url, second);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
