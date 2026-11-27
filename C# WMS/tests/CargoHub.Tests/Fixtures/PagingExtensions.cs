using System.Net.Http.Json;

namespace CargoHub.Tests.Fixtures;

/// <summary>A list response: one page of rows plus the paging numbers.</summary>
public sealed record PagedResponse<T>(List<T> Items, int Page, int Size, int Total, int TotalPages);

public static class PagingExtensions
{
    /// <summary>Reads one page of a list endpoint; pass the query string as part of the url.</summary>
    public static async Task<PagedResponse<T>> GetPageAsync<T>(this HttpClient client, string url)
    {
        var page = await client.GetFromJsonAsync<PagedResponse<T>>(url);
        Assert.NotNull(page);
        return page;
    }

    /// <summary>Reads every row of a list endpoint by following the pages, oldest update first.</summary>
    public static async Task<List<T>> GetAllAsync<T>(this HttpClient client, string url)
    {
        var separator = url.Contains('?') ? '&' : '?';
        var rows = new List<T>();
        for (var number = 1; ; number++)
        {
            var page = await client.GetPageAsync<T>($"{url}{separator}page={number}&size=100&sort=asc");
            rows.AddRange(page.Items);
            if (number >= page.TotalPages)
                return rows;
        }
    }
}
