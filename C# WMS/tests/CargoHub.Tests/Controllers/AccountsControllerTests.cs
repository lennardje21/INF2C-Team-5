using System.Net;
using System.Net.Http.Json;
using CargoHub.Api.Authentication;
using CargoHub.Api.Models;
using CargoHub.Tests.Fixtures;

namespace CargoHub.Tests.Controllers;

// The account endpoints follow the role hierarchy instead of endpoint permissions: the
// superroot manages roots, a root manages admins, an admin manages floormanagers and users,
// each within its own warehouse (the superroot in every warehouse). Every test creates its
// own accounts below the fixture's superroot, so the tests do not depend on each other.
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

    // One account of every role in a warehouse; each created by the role that manages it.
    private sealed record Team(Member Root, Member Admin, Member Floormanager, Member User)
    {
        public Member this[string role] => role switch
        {
            "root" => Root,
            "admin" => Admin,
            "floormanager" => Floormanager,
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

    private HttpClient Caller(Team team, string role) => role == "superroot" ? _superroot : team[role].Client;

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
        var floormanager = await CreateAsync(admin.Client, NewAccount("floormanager"));
        var user = await CreateAsync(admin.Client, NewAccount("user"));
        return new Team(root, admin, floormanager, user);
    }

    private static async Task<List<int>> GetIdsAsync(HttpClient client) =>
        (await client.GetAllAsync<Account>(Url)).Select(a => a.Id).ToList();

    // Per role: what it may do (FR-3.1, FR-3.2)

    [Theory]
    [InlineData("superroot", "root")]
    [InlineData("root", "admin")]
    [InlineData("admin", "floormanager")]
    [InlineData("admin", "user")]
    public async Task ManagedRole_CanBeCreatedReadUpdatedAndDeleted(string callerRole, string role)
    {
        var team = await CreateTeamAsync(Warehouse);
        var caller = Caller(team, callerRole);

        var created = await CreateAsync(caller, NewAccount(role, callerRole == "superroot" ? Warehouse : null));
        Assert.Equal(role, created.Account.Role);
        Assert.Equal(Warehouse, created.Account.WarehouseId);
        Assert.Equal(created.Id, (await created.Client.GetFromJsonAsync<Account>($"{Url}/me"))!.Id);

        Assert.Equal(HttpStatusCode.OK, (await caller.GetAsync($"{Url}/{created.Id}")).StatusCode);
        Assert.Contains(created.Id, await GetIdsAsync(caller));

        var fields = Fields(created.Account);
        fields["name"] = "Changed Name";
        Assert.Equal(HttpStatusCode.NoContent, (await caller.PutAsJsonAsync($"{Url}/{created.Id}", fields)).StatusCode);
        Assert.Equal("Changed Name", (await caller.GetFromJsonAsync<Account>($"{Url}/{created.Id}"))!.Name);

        Assert.Equal(HttpStatusCode.NoContent, (await caller.DeleteAsync($"{Url}/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task Admin_CanChangeAUserIntoAFloormanager()
    {
        var team = await CreateTeamAsync(Warehouse);
        var fields = Fields(team.User.Account);
        fields["role"] = "floormanager";

        var response = await team.Admin.Client.PutAsJsonAsync($"{Url}/{team.User.Id}", fields);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("floormanager", (await team.User.Client.GetFromJsonAsync<Account>($"{Url}/me"))!.Role);
    }

    [Fact]
    public async Task CreateAccount_ReturnsAWorkingKeyOnlyOnce()
    {
        var response = await _superroot.PostAsJsonAsync(Url, NewAccount("root", Warehouse));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<CreatedAccount>())!;
        Assert.Equal($"{Url}/{created.Id}", response.Headers.Location!.AbsolutePath);
        Assert.Equal(64, created.ApiKey.Length);

        var json = await _superroot.GetStringAsync($"{Url}/{created.Id}");
        Assert.DoesNotContain("apiKey", json);
        Assert.DoesNotContain("hash", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAccount_DefaultAccessFollowsTheRole()
    {
        var team = await CreateTeamAsync(Warehouse);

        var admin = team.Admin.Account.EndpointAccess!["orders"];
        Assert.True(admin.Get && admin.Post && admin.Put && admin.Delete);

        foreach (var member in new[] { team.Floormanager, team.User })
        {
            var access = member.Account.EndpointAccess!["orders"];
            Assert.True(access.Get);
            Assert.False(access.Post || access.Put || access.Delete);
        }
    }

    // Per role: what it may not do (403)

    [Theory]
    [InlineData("superroot", "superroot")]
    [InlineData("superroot", "admin")]
    [InlineData("superroot", "user")]
    [InlineData("root", "root")]
    [InlineData("root", "user")]
    [InlineData("root", "floormanager")]
    [InlineData("admin", "admin")]
    [InlineData("admin", "root")]
    [InlineData("floormanager", "user")]
    [InlineData("user", "user")]
    public async Task CreateAccount_WithARoleTheCallerDoesNotManage_ReturnsForbidden(string callerRole, string role)
    {
        var team = await CreateTeamAsync(Warehouse);
        var warehouseId = callerRole == "superroot" && role != "superroot" ? Warehouse : (int?)null;

        var response = await Caller(team, callerRole).PostAsJsonAsync(Url, NewAccount(role, warehouseId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("superroot", "admin")]
    [InlineData("root", "user")]
    [InlineData("root", "floormanager")]
    [InlineData("admin", "root")]
    [InlineData("floormanager", "user")]
    public async Task AccountsTheCallerDoesNotManage_CannotBeChangedOrDeleted(string callerRole, string targetRole)
    {
        var team = await CreateTeamAsync(Warehouse);
        var caller = Caller(team, callerRole);
        var target = team[targetRole];

        Assert.Equal(HttpStatusCode.Forbidden,
            (await caller.PutAsJsonAsync($"{Url}/{target.Id}", Fields(target.Account))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await caller.DeleteAsync($"{Url}/{target.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await caller.PostAsync($"{Url}/{target.Id}/api_key", null)).StatusCode);
    }

    [Fact]
    public async Task Admin_CannotManageOtherAdmins()
    {
        var team = await CreateTeamAsync(Warehouse);
        var otherAdmin = await CreateAsync(team.Root.Client, NewAccount("admin"));
        var url = $"{Url}/{otherAdmin.Id}";

        Assert.Equal(HttpStatusCode.Forbidden, (await team.Admin.Client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await team.Admin.Client.PutAsJsonAsync(url, Fields(otherAdmin.Account))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await team.Admin.Client.DeleteAsync(url)).StatusCode);
    }

    [Theory]
    [InlineData("admin", "user", "admin")]
    [InlineData("root", "admin", "root")]
    public async Task UpdateAccount_ToARoleTheCallerDoesNotManage_ReturnsForbidden(
        string callerRole, string targetRole, string newRole)
    {
        var team = await CreateTeamAsync(Warehouse);
        var fields = Fields(team[targetRole].Account);
        fields["role"] = newRole;

        var response = await team[callerRole].Client.PutAsJsonAsync($"{Url}/{team[targetRole].Id}", fields);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task LowerRoles_CannotSeeHigherRoles()
    {
        var team = await CreateTeamAsync(Warehouse);

        Assert.Equal(HttpStatusCode.Forbidden, (await team.User.Client.GetAsync($"{Url}/{team.Admin.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await team.Admin.Client.GetAsync($"{Url}/{team.Root.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await team.Floormanager.Client.GetAsync($"{Url}/{team.User.Id}")).StatusCode);
    }

    [Fact]
    public async Task GetAccounts_ShowsTheOwnAccountAndEverythingBelowItReadOnly()
    {
        var team = await CreateTeamAsync(Warehouse);

        var root = await GetIdsAsync(team.Root.Client);
        Assert.Contains(team.Root.Id, root);
        Assert.Contains(team.Admin.Id, root);
        Assert.Contains(team.Floormanager.Id, root);
        Assert.Contains(team.User.Id, root);

        // A root sees the users of its warehouse, but only manages admins.
        Assert.Equal(HttpStatusCode.OK, (await team.Root.Client.GetAsync($"{Url}/{team.User.Id}")).StatusCode);

        var admin = await GetIdsAsync(team.Admin.Client);
        Assert.DoesNotContain(team.Root.Id, admin);
        Assert.Contains(team.User.Id, admin);

        Assert.Equal([team.User.Id], await GetIdsAsync(team.User.Client));
        Assert.Equal([team.Floormanager.Id], await GetIdsAsync(team.Floormanager.Client));
    }

    // Another warehouse (403)

    [Theory]
    [InlineData("root", "admin")]
    [InlineData("admin", "user")]
    public async Task CreateAccount_InOtherWarehouse_ReturnsForbidden(string callerRole, string role)
    {
        var team = await CreateTeamAsync(Warehouse);

        var response = await team[callerRole].Client.PostAsJsonAsync(Url, NewAccount(role, OtherWarehouse));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("root", "admin")]
    [InlineData("admin", "user")]
    public async Task AccountsInOtherWarehouse_ReturnForbidden(string callerRole, string targetRole)
    {
        var team = await CreateTeamAsync(Warehouse);
        var other = await CreateTeamAsync(OtherWarehouse);
        var caller = team[callerRole].Client;
        var target = other[targetRole];
        var url = $"{Url}/{target.Id}";

        Assert.Equal(HttpStatusCode.Forbidden, (await caller.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await caller.PutAsJsonAsync(url, Fields(target.Account))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await caller.DeleteAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await caller.PostAsync($"{url}/api_key", null)).StatusCode);
        Assert.DoesNotContain(target.Id, await GetIdsAsync(caller));
    }

    [Fact]
    public async Task UpdateAccount_ToOtherWarehouse_ReturnsForbidden()
    {
        var team = await CreateTeamAsync(Warehouse);
        var fields = Fields(team.User.Account);
        fields["warehouseId"] = OtherWarehouse;

        var response = await team.Admin.Client.PutAsJsonAsync($"{Url}/{team.User.Id}", fields);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // The own account

    [Theory]
    [InlineData("root")]
    [InlineData("admin")]
    [InlineData("floormanager")]
    [InlineData("user")]
    public async Task OwnAccount_CannotBeChangedOrDeleted(string role)
    {
        var team = await CreateTeamAsync(Warehouse);
        var member = team[role];
        var fields = Fields(member.Account);
        fields["name"] = "Changed Name";

        Assert.Equal(HttpStatusCode.Forbidden,
            (await member.Client.PutAsJsonAsync($"{Url}/{member.Id}", fields)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.Client.DeleteAsync($"{Url}/{member.Id}")).StatusCode);
        Assert.Equal(member.Account.Name, (await member.Client.GetFromJsonAsync<Account>($"{Url}/me"))!.Name);
    }

    [Fact]
    public async Task Superroot_CannotBeChangedOrDeleted()
    {
        var team = await CreateTeamAsync(Warehouse);
        var superroot = (await _superroot.GetFromJsonAsync<Account>($"{Url}/me"))!;

        Assert.Equal(HttpStatusCode.Forbidden, (await _superroot.DeleteAsync($"{Url}/{superroot.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await team.Root.Client.DeleteAsync($"{Url}/{superroot.Id}")).StatusCode);
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

    // Soft delete (FR-3.3)

    [Fact]
    public async Task DeleteAccount_IsASoftDelete()
    {
        var team = await CreateTeamAsync(Warehouse);
        var email = team.User.Account.Email;

        Assert.Equal(HttpStatusCode.NoContent, (await team.Admin.Client.DeleteAsync($"{Url}/{team.User.Id}")).StatusCode);

        // The key no longer works and the account is gone from the API...
        Assert.Equal(HttpStatusCode.Unauthorized, (await team.User.Client.GetAsync($"{Url}/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await team.Admin.Client.GetAsync($"{Url}/{team.User.Id}")).StatusCode);
        Assert.DoesNotContain(team.User.Id, await GetIdsAsync(team.Admin.Client));
        Assert.Equal(HttpStatusCode.NotFound, (await team.Admin.Client.DeleteAsync($"{Url}/{team.User.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await team.Admin.Client.PutAsJsonAsync($"{Url}/{team.User.Id}", Fields(team.User.Account))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await team.Admin.Client.PostAsync($"{Url}/{team.User.Id}/api_key", null)).StatusCode);

        // ...but stays in the database, without name, email or key.
        Assert.Equal(1, await fixture.QueryIntAsync(
            "SELECT COUNT(*) FROM `account` WHERE `id` = @id AND `deleted_at` IS NOT NULL AND `name` IS NULL " +
            "AND `email` IS NULL AND `api_key_hash` IS NULL", ("@id", team.User.Id)));

        // The email address can be used again.
        var again = NewAccount("user");
        again["email"] = email;
        await CreateAsync(team.Admin.Client, again);
    }

    // Validation (FR-3.4)

    [Fact]
    public async Task CreateAccount_ExistingEmail_ReturnsConflict()
    {
        var team = await CreateTeamAsync(Warehouse);
        var account = NewAccount("user");
        account["email"] = team.User.Account.Email.ToUpperInvariant();

        var response = await team.Admin.Client.PostAsJsonAsync(Url, account);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UpdateAccount_ExistingEmail_ReturnsConflict()
    {
        var team = await CreateTeamAsync(Warehouse);
        var fields = Fields(team.User.Account);
        fields["email"] = team.Floormanager.Account.Email;

        var response = await team.Admin.Client.PutAsJsonAsync($"{Url}/{team.User.Id}", fields);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("role", "boss")]
    [InlineData("name", "")]
    [InlineData("name", "R2-D2")]
    [InlineData("email", "")]
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
}
