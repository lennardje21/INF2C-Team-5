using System.ComponentModel.DataAnnotations;
using CargoHub.Api.Data;
using CargoHub.Api.Validation;

namespace CargoHub.Tests.Unit;

public sealed class ListQueryTests
{
    private static List<ValidationResult> Validate(ListQuery query) =>
        query.Validate(new ValidationContext(query)).ToList();

    [Fact]
    public void NoParameters_GivePageOneSize50NewestFirst()
    {
        var request = new ListQuery().ToPageRequest();

        Assert.Equal(new PageRequest(1, 50, null, null, SortDirection.Desc), request);
        Assert.Empty(Validate(new ListQuery()));
    }

    [Fact]
    public void ValidParameters_AreParsed()
    {
        var query = new ListQuery { Page = 2, Size = 100, From = "2026-01-01", To = "2026-01-31", Sort = "asc" };

        var request = query.ToPageRequest();

        Assert.Equal(new PageRequest(2, 100, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), SortDirection.Asc),
            request);
        Assert.Empty(Validate(query));
    }

    [Theory]
    [InlineData("asc", SortDirection.Asc)]
    [InlineData("ASC", SortDirection.Asc)]
    [InlineData("desc", SortDirection.Desc)]
    [InlineData("Desc", SortDirection.Desc)]
    public void Sort_IsNotCaseSensitive(string sort, SortDirection expected) =>
        Assert.Equal(expected, new ListQuery { Sort = sort }.ToPageRequest().Sort);

    [Theory]
    [InlineData(1, true)]
    [InlineData(50, true)]
    [InlineData(100, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(101, false)]
    public void Size_MustBeBetween1And100(int size, bool valid) =>
        Assert.Equal(valid, Validate(new ListQuery { Size = size }).Count == 0);

    [Theory]
    [InlineData(1, true)]
    [InlineData(int.MaxValue, true)]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    public void Page_MustBeAtLeast1(int page, bool valid) =>
        Assert.Equal(valid, Validate(new ListQuery { Page = page }).Count == 0);

    [Theory]
    [InlineData("2026-01-31", true)]
    [InlineData("0001-01-01", true)]
    [InlineData("9999-12-31", true)]
    [InlineData("2024-02-29", true)]
    [InlineData("2026-02-29", false)]
    [InlineData("2026-13-01", false)]
    [InlineData("2026-1-1", false)]
    [InlineData("26-01-31", false)]
    [InlineData("01-31-2026", false)]
    [InlineData("31/01/2026", false)]
    [InlineData("20260131", false)]
    [InlineData("2026-01-31T00:00:00Z", false)]
    [InlineData(" 2026-01-31", false)]
    [InlineData("today", false)]
    public void Dates_MustBeIso8601Days(string date, bool valid)
    {
        Assert.Equal(valid, Validate(new ListQuery { From = date }).Count == 0);
        Assert.Equal(valid, Validate(new ListQuery { To = date }).Count == 0);
    }

    [Fact]
    public void FromAfterTo_IsInvalid_ButTheSameDayIsFine()
    {
        var after = Validate(new ListQuery { From = "2026-02-01", To = "2026-01-31" });
        var sameDay = Validate(new ListQuery { From = "2026-01-31", To = "2026-01-31" });

        Assert.Equal(["from"], Assert.Single(after).MemberNames);
        Assert.Empty(sameDay);
    }

    [Theory]
    [InlineData("up")]
    [InlineData("ascending")]
    [InlineData("updated_at")]
    [InlineData("")]
    public void Sort_OtherThanAscOrDesc_IsInvalid(string sort) =>
        Assert.Equal(["sort"], Assert.Single(Validate(new ListQuery { Sort = sort })).MemberNames);

    [Fact]
    public void EveryInvalidParameter_IsReportedByName()
    {
        var errors = Validate(new ListQuery { Page = 0, Size = 0, From = "x", To = "y", Sort = "up" });

        var names = errors.SelectMany(error => error.MemberNames).Order().ToArray();
        Assert.Equal(["from", "page", "size", "sort", "to"], names);
        Assert.All(errors, error => Assert.Contains(error.MemberNames.Single(), error.ErrorMessage));
    }

    [Fact]
    public void ToPageRequest_ThrowsWithTheMessageWhenInvalid()
    {
        var error = Assert.Throws<ArgumentException>(() => new ListQuery { Size = 500 }.ToPageRequest());

        Assert.Contains("size", error.Message);
    }
}
