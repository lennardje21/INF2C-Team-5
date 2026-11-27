using CargoHub.Api.Data;

namespace CargoHub.Tests.Unit;

public sealed class PageRequestTests
{
    private sealed record Row(int Id, DateTime UpdatedAt);

    private static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0, int second = 0) =>
        new(year, month, day, hour, minute, second, DateTimeKind.Utc);

    private static readonly Row[] Rows =
    [
        new(1, Utc(2026, 1, 30, 23, 59, 59)),
        new(2, Utc(2026, 1, 31, 0, 0, 0)),
        new(3, Utc(2026, 1, 31, 12, 0, 0)),
        new(4, Utc(2026, 1, 31, 23, 59, 59).AddTicks(TimeSpan.TicksPerSecond - 1)),
        new(5, Utc(2026, 2, 1, 0, 0, 0)),
        new(6, Utc(2026, 2, 2, 0, 0, 0))
    ];

    private static List<int> Ids(PagedResult<Row> page) => page.Items.Select(row => row.Id).ToList();

    [Fact]
    public void Defaults_ArePage1Size50NewestFirst()
    {
        var request = new PageRequest();

        Assert.Equal((1, 50, SortDirection.Desc), (request.Page, request.Size, request.Sort));
        Assert.Null(request.From);
        Assert.Null(request.To);
    }

    [Fact]
    public void Offset_DoesNotOverflowForHugePages() =>
        Assert.Equal((long)(int.MaxValue - 1) * 100, new PageRequest(int.MaxValue, 100).Offset);

    [Fact]
    public void To_CountsTheWholeDay()
    {
        var request = new PageRequest(From: new DateOnly(2026, 1, 31), To: new DateOnly(2026, 1, 31));

        Assert.Equal(Utc(2026, 1, 31), request.FromInclusive);
        Assert.Equal(Utc(2026, 2, 1), request.ToExclusive);
        Assert.Equal(DateTimeKind.Utc, request.ToExclusive!.Value.Kind);
    }

    [Fact]
    public void ToOnTheLastDayOfTheCalendar_HasNoUpperBound() =>
        Assert.Null(new PageRequest(To: DateOnly.MaxValue).ToExclusive);

    [Fact]
    public void Apply_FiltersOnUpdatedAtIncludingTheWholeToDay()
    {
        var page = new PageRequest(From: new DateOnly(2026, 1, 31), To: new DateOnly(2026, 1, 31), Sort: SortDirection.Asc)
            .Apply(Rows, row => row.UpdatedAt);

        Assert.Equal([2, 3, 4], Ids(page));
        Assert.Equal(3, page.Total);
    }

    [Theory]
    [InlineData(SortDirection.Asc, new[] { 1, 2, 3, 4, 5, 6 })]
    [InlineData(SortDirection.Desc, new[] { 6, 5, 4, 3, 2, 1 })]
    public void Apply_SortsOnUpdatedAt(SortDirection sort, int[] expected) =>
        Assert.Equal(expected, Ids(new PageRequest(Sort: sort).Apply(Rows.AsEnumerable().Reverse(), row => row.UpdatedAt)));

    [Fact]
    public void Apply_CutsOutThePageAndKeepsTheTotal()
    {
        var request = new PageRequest(2, 4, Sort: SortDirection.Asc);

        var page = request.Apply(Rows, row => row.UpdatedAt);

        Assert.Equal([5, 6], Ids(page));
        Assert.Equal((2, 4, 6, 2), (page.Page, page.Size, page.Total, page.TotalPages));
    }

    [Fact]
    public void Apply_BeyondTheEnd_GivesAnEmptyPage()
    {
        var page = new PageRequest(int.MaxValue, 100).Apply(Rows, row => row.UpdatedAt);

        Assert.Empty(page.Items);
        Assert.Equal(6, page.Total);
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(-1, 50)]
    public void EnsureValid_RejectsAPageBelow1(int page, int size) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PageRequest(page, size).EnsureValid());

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void EnsureValid_RejectsASizeOutside1To100(int size) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PageRequest(1, size).EnsureValid());

    [Fact]
    public void EnsureValid_RejectsFromAfterTo() =>
        Assert.Throws<ArgumentException>(() =>
            new PageRequest(From: new DateOnly(2026, 2, 1), To: new DateOnly(2026, 1, 31)).EnsureValid());

    [Theory]
    [InlineData(0, 50, 0)]
    [InlineData(1, 50, 1)]
    [InlineData(50, 50, 1)]
    [InlineData(51, 50, 2)]
    [InlineData(6132, 50, 123)]
    public void TotalPages_RoundsUp(int total, int size, int expected) =>
        Assert.Equal(expected, new PagedResult<int>([], 1, size, total).TotalPages);
}
