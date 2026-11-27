namespace CargoHub.Api.Data;

public enum SortDirection
{
    Asc,
    Desc
}

/// <summary>
/// Which page of a list to return. Page starts at 1; From and To are UTC days and filter on
/// updated_at, with To counting the whole day.
/// </summary>
public sealed record PageRequest(
    int Page = PageRequest.DefaultPage,
    int Size = PageRequest.DefaultSize,
    DateOnly? From = null,
    DateOnly? To = null,
    SortDirection Sort = SortDirection.Desc)
{
    public const int DefaultPage = 1;
    public const int DefaultSize = 50;
    public const int MaxSize = 100;

    // Inclusive start of the From day.
    public DateTime? FromInclusive => From?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    // Start of the day after To, so everything on the To day still counts. There is no day after
    // 9999-12-31, and nothing is stored later than that, so it needs no upper bound.
    public DateTime? ToExclusive => To is { } to && to != DateOnly.MaxValue
        ? to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) : null;

    public long Offset => (long)(Page - 1) * Size;

    /// <summary>Throws when the request is outside what the repository can serve.</summary>
    public void EnsureValid()
    {
        if (Page < 1)
            throw new ArgumentOutOfRangeException(nameof(Page), Page, "Page starts at 1.");
        if (Size is < 1 or > MaxSize)
            throw new ArgumentOutOfRangeException(nameof(Size), Size, $"Size must be between 1 and {MaxSize}.");
        if (From > To)
            throw new ArgumentException("From must not be after To.", nameof(From));
    }

    /// <summary>Applies the same filter, sort and page to a list that is already in memory.</summary>
    public PagedResult<T> Apply<T>(IEnumerable<T> source, Func<T, DateTime> updatedAt)
    {
        EnsureValid();
        var rows = source.Where(row =>
            (FromInclusive is not { } from || updatedAt(row) >= from) &&
            (ToExclusive is not { } to || updatedAt(row) < to));
        var ordered = Sort == SortDirection.Asc
            ? rows.OrderBy(updatedAt) : rows.OrderByDescending(updatedAt);
        var all = ordered.ToList();
        return new PagedResult<T>(all.Skip((int)Math.Min(Offset, all.Count)).Take(Size).ToList(),
            Page, Size, all.Count);
    }
}

/// <summary>One page of a list plus the numbers a client needs to ask for the next one.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int Size, int Total)
{
    public int TotalPages => (int)Math.Ceiling(Total / (double)Size);

    /// <summary>Same paging numbers with other items, e.g. after loading child rows.</summary>
    public PagedResult<TOut> Map<TOut>(Func<IReadOnlyList<T>, IReadOnlyList<TOut>> map) =>
        new(map(Items), Page, Size, Total);
}
