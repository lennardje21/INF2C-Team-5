using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CargoHub.Api.Models;
using CargoHub.Tests.Fixtures;

namespace CargoHub.Tests.Controllers;

// FR-1: page, size, from, to and sort on the list endpoints.
[Collection("Api")]
public sealed class PagingControllerTests(ApiFixture fixture)
{
    private const string Base = "/api/v2";
    private readonly HttpClient _client = fixture.Client;

    // Three list endpoints with plenty of seeded rows; the second value is the table behind it.
    public static TheoryData<string, string> Endpoints => new()
    {
        { "items", "item" },
        { "orders", "order" },
        { "shipments", "shipment" }
    };

    public static TheoryData<string> AllListEndpoints => new()
    {
        "accounts", "clients", "inventories", "item_groups", "item_lines", "item_types", "items",
        "locations", "orders", "shipments", "suppliers", "transfers", "warehouses"
    };

    private async Task<JsonElement> GetOkAsync(string url)
    {
        using var response = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static List<int> Ids(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetInt32()).ToList();

    private Task<int> CountAsync(string table) => fixture.QueryIntAsync($"SELECT COUNT(*) FROM `{table}`");

    // Defaults: page 1, size 50, newest first

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task NoParameters_ReturnsTheFirstPageOf50NewestFirst(string resource, string table)
    {
        var total = await CountAsync(table);

        var page = await GetOkAsync($"{Base}/{resource}");

        Assert.Equal(1, page.GetProperty("page").GetInt32());
        Assert.Equal(50, page.GetProperty("size").GetInt32());
        Assert.Equal(total, page.GetProperty("total").GetInt32());
        Assert.Equal((int)Math.Ceiling(total / 50.0), page.GetProperty("totalPages").GetInt32());
        Assert.Equal(
            await fixture.QueryIntListAsync(
                $"SELECT `id` FROM `{table}` ORDER BY `updated_at` DESC, `id` DESC LIMIT 50"),
            Ids(page));
    }

    [Fact]
    public async Task Response_HasExactlyTheDocumentedFields()
    {
        var page = await GetOkAsync($"{Base}/items?size=1");

        Assert.Equal(
            ["items", "page", "size", "total", "totalPages"],
            page.EnumerateObject().Select(property => property.Name).Order().ToArray());
    }

    [Theory]
    [MemberData(nameof(AllListEndpoints))]
    public async Task EveryListEndpoint_ReturnsThePagedFormat(string resource)
    {
        var page = await GetOkAsync($"{Base}/{resource}?size=2");

        var rows = page.GetProperty("items").GetArrayLength();
        var total = page.GetProperty("total").GetInt32();
        Assert.InRange(rows, 1, 2);
        Assert.Equal(1, page.GetProperty("page").GetInt32());
        Assert.Equal(2, page.GetProperty("size").GetInt32());
        Assert.True(total >= rows);
        Assert.Equal((int)Math.Ceiling(total / 2.0), page.GetProperty("totalPages").GetInt32());
    }

    // Combinations

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task FromToSortPageAndSize_CanBeCombined(string resource, string table)
    {
        // A window around the middle of the data, so that there are well over two pages of 10.
        var count = await CountAsync(table);
        var from = await fixture.QueryStringAsync(
            $"SELECT DATE_FORMAT(`updated_at`, '%Y-%m-%d') FROM `{table}` ORDER BY `updated_at` LIMIT 1 OFFSET @offset",
            ("@offset", count / 4));
        var to = await fixture.QueryStringAsync(
            $"SELECT DATE_FORMAT(`updated_at`, '%Y-%m-%d') FROM `{table}` ORDER BY `updated_at` LIMIT 1 OFFSET @offset",
            ("@offset", count * 3 / 4));
        const string window = "`updated_at` >= @from AND `updated_at` < DATE_ADD(@to, INTERVAL 1 DAY)";
        var parameters = new[] { ("@from", (object)from), ("@to", to) };
        var expectedTotal = await fixture.QueryIntAsync($"SELECT COUNT(*) FROM `{table}` WHERE {window}", parameters);
        Assert.True(expectedTotal > 20);

        var page = await GetOkAsync($"{Base}/{resource}?from={from}&to={to}&sort=asc&page=2&size=10");

        Assert.Equal(2, page.GetProperty("page").GetInt32());
        Assert.Equal(10, page.GetProperty("size").GetInt32());
        Assert.Equal(expectedTotal, page.GetProperty("total").GetInt32());
        Assert.Equal((int)Math.Ceiling(expectedTotal / 10.0), page.GetProperty("totalPages").GetInt32());
        Assert.Equal(
            await fixture.QueryIntListAsync(
                $"SELECT `id` FROM `{table}` WHERE {window} ORDER BY `updated_at` ASC, `id` ASC LIMIT 10 OFFSET 10",
                parameters),
            Ids(page));
    }

    [Theory]
    [InlineData("asc")]
    [InlineData("desc")]
    [InlineData("ASC")]
    public async Task Sort_OrdersByUpdatedAt(string sort)
    {
        var page = await GetOkAsync($"{Base}/items?sort={sort}&size=100");

        var updated = page.GetProperty("items").EnumerateArray()
            .Select(row => row.GetProperty("updatedAt").GetDateTime()).ToList();
        Assert.Equal(sort.Equals("asc", StringComparison.OrdinalIgnoreCase)
            ? updated.Order() : updated.OrderDescending(), updated);
    }

    [Fact]
    public async Task To_CountsTheWholeDay_AndFromStartsAtMidnight()
    {
        using var createResponse = await _client.PostAsJsonAsync($"{Base}/item_groups",
            new { name = $"Paging test {Guid.NewGuid():N}", description = "Paging test" });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = (await createResponse.Content.ReadFromJsonAsync<ItemGroup>())!;
        var day = DateOnly.FromDateTime(created.UpdatedAt);
        string Day(int offset) => day.AddDays(offset).ToString("yyyy-MM-dd");

        Assert.Contains(created.Id, Ids(await GetOkAsync($"{Base}/item_groups?from={Day(0)}&to={Day(0)}&size=100")));
        Assert.DoesNotContain(created.Id, Ids(await GetOkAsync($"{Base}/item_groups?to={Day(-1)}&size=100")));
        Assert.DoesNotContain(created.Id, Ids(await GetOkAsync($"{Base}/item_groups?from={Day(1)}&size=100")));
    }

    // Edges of the paging

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task LastPage_HoldsTheRemainingRows(string resource, string table)
    {
        var total = await CountAsync(table);
        var pages = (int)Math.Ceiling(total / 7.0);

        var page = await GetOkAsync($"{Base}/{resource}?size=7&page={pages}");

        Assert.Equal(pages, page.GetProperty("totalPages").GetInt32());
        Assert.Equal(total - (pages - 1) * 7, page.GetProperty("items").GetArrayLength());
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task PageBeyondTheEnd_ReturnsAnEmptyListWithStatus200(string resource, string table)
    {
        var total = await CountAsync(table);
        var pages = (int)Math.Ceiling(total / 50.0);

        var page = await GetOkAsync($"{Base}/{resource}?page={pages + 1}");

        Assert.Empty(page.GetProperty("items").EnumerateArray());
        Assert.Equal(pages + 1, page.GetProperty("page").GetInt32());
        Assert.Equal(total, page.GetProperty("total").GetInt32());
        Assert.Equal(pages, page.GetProperty("totalPages").GetInt32());
    }

    [Fact]
    public async Task HugePageNumber_ReturnsAnEmptyListInsteadOfOverflowing()
    {
        var page = await GetOkAsync($"{Base}/items?page={int.MaxValue}&size=100");

        Assert.Empty(page.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task FilterWithoutMatches_ReturnsAnEmptyListWithTotalZero()
    {
        var page = await GetOkAsync($"{Base}/items?from=2999-01-01");

        Assert.Empty(page.GetProperty("items").EnumerateArray());
        Assert.Equal(0, page.GetProperty("total").GetInt32());
        Assert.Equal(0, page.GetProperty("totalPages").GetInt32());
    }

    [Fact]
    public async Task Size100_IsAllowed()
    {
        var page = await GetOkAsync($"{Base}/items?size=100");

        Assert.Equal(100, page.GetProperty("size").GetInt32());
        Assert.Equal(100, page.GetProperty("items").GetArrayLength());
    }

    [Theory]
    [InlineData("from=&to=&sort=&page=&size=")]
    [InlineData("from=2026-01-31&to=2026-01-31")]
    [InlineData("from=0001-01-01&to=9999-12-31")]
    public async Task EmptyOrExtremeValues_AreAccepted(string query)
    {
        await GetOkAsync($"{Base}/items?{query}");
    }

    [Fact]
    public async Task EachPage_ContinuesWhereThePreviousEnded()
    {
        var all = new List<int>();
        for (var number = 1; number <= 3; number++)
            all.AddRange(Ids(await GetOkAsync($"{Base}/item_lines?size=5&page={number}")));

        var expected = await fixture.QueryIntListAsync(
            "SELECT `id` FROM `item_line` ORDER BY `updated_at` DESC, `id` DESC LIMIT 15");
        Assert.Equal(expected, all);
    }

    [Fact]
    public async Task Accounts_PageThroughWhatTheCallerMaySee()
    {
        var first = await _client.GetPageAsync<Account>($"{Base}/accounts?size=1&sort=asc");
        var all = await _client.GetAllAsync<Account>($"{Base}/accounts");

        Assert.Single(first.Items);
        Assert.Equal(first.Total, all.Count);
        Assert.Equal(all[0].Id, first.Items[0].Id);
        Assert.Equal(all.Count, all.Select(account => account.Id).Distinct().Count());
    }

    [Fact]
    public async Task NestedLists_KeepTheirPlainFormat()
    {
        using var response = await _client.GetAsync($"{Base}/orders/1/items");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
    }

    // Validation (FR-1.4): 400, and the message names the parameter

    [Theory]
    [InlineData("page=0", "page")]
    [InlineData("page=-1", "page")]
    [InlineData("page=abc", "page")]
    [InlineData("page=99999999999", "page")]
    [InlineData("page=1.5", "page")]
    [InlineData("size=0", "size")]
    [InlineData("size=-1", "size")]
    [InlineData("size=101", "size")]
    [InlineData("size=abc", "size")]
    [InlineData("from=yesterday", "from")]
    [InlineData("from=01-01-2026", "from")]
    [InlineData("from=2026-1-1", "from")]
    [InlineData("from=2026-02-30", "from")]
    [InlineData("from=2026-13-01", "from")]
    [InlineData("from=2026-01-01T10:00:00Z", "from")]
    [InlineData("to=abc", "to")]
    [InlineData("to=31/01/2026", "to")]
    [InlineData("from=2026-02-01&to=2026-01-31", "from")]
    [InlineData("sort=up", "sort")]
    [InlineData("sort=ascending", "sort")]
    [InlineData("sort=updated_at", "sort")]
    public async Task InvalidParameter_ReturnsBadRequestNamingTheParameter(string query, string parameter)
    {
        using var response = await _client.GetAsync($"{Base}/items?{query}");

        using var _ = await AssertBadRequestAsync(response, parameter);
    }

    [Theory]
    [MemberData(nameof(AllListEndpoints))]
    public async Task EveryListEndpoint_RejectsInvalidParameters(string resource)
    {
        foreach (var (query, parameter) in new[] { ("size=101", "size"), ("sort=up", "sort"), ("from=x", "from") })
        {
            using var response = await _client.GetAsync($"{Base}/{resource}?{query}");
            using var _ = await AssertBadRequestAsync(response, parameter);
        }
    }

    [Fact]
    public async Task SeveralInvalidParameters_AreAllReported()
    {
        using var response = await _client.GetAsync($"{Base}/items?page=0&size=0&sort=up");

        using var document = await AssertBadRequestAsync(response, "page");
        var errors = document.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("size", out _));
        Assert.True(errors.TryGetProperty("sort", out _));
    }

    private static async Task<JsonDocument> AssertBadRequestAsync(HttpResponseMessage response, string parameter)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, document.RootElement.GetProperty("status").GetInt32());
        var errors = document.RootElement.GetProperty("errors").EnumerateObject().ToList();
        var error = Assert.Single(errors, e => e.Name.Equals(parameter, StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(error.Value.EnumerateArray());
        return document;
    }

    // Documentation (FR-1.3): /docs is generated from /openapi/v2.json

    [Fact]
    public async Task OpenApi_DescribesTheParametersAndThePagedResponse()
    {
        using var client = fixture.CreateUnauthenticatedClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v2.json"));
        var root = document.RootElement;

        var get = root.GetProperty("paths").GetProperty("/api/v2/items").GetProperty("get");
        Assert.Equal(
            ["from", "page", "size", "sort", "to"],
            get.GetProperty("parameters").EnumerateArray()
                .Select(parameter => parameter.GetProperty("name").GetString()!).Order().ToArray());
        var response = get.GetProperty("responses").GetProperty("200").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString();
        Assert.EndsWith("/PagedResultOfItem", response);
        Assert.True(get.GetProperty("responses").TryGetProperty("400", out _));

        var schema = root.GetProperty("components").GetProperty("schemas").GetProperty("PagedResultOfItem");
        Assert.Equal(
            ["items", "page", "size", "total", "totalPages"],
            schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order().ToArray());
    }
}
