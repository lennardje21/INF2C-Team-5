using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using CargoHub.Api.Data;
using Microsoft.AspNetCore.Mvc;

namespace CargoHub.Api.Validation;

/// <summary>
/// The query parameters every list endpoint accepts: page, size, from, to and sort.
/// MVC validates this when it binds the request, so a bad value gives a 400 before the
/// action runs; the message names the parameter. Use ToPageRequest() in the action.
/// </summary>
public sealed class ListQuery : IValidatableObject
{
    private const string DateFormat = "yyyy-MM-dd";

    [FromQuery(Name = "page")]
    [Description("Page number, starting at 1. Default 1.")]
    public int? Page { get; set; }

    [FromQuery(Name = "size")]
    [Description("Rows per page, 1 to 100. Default 50.")]
    public int? Size { get; set; }

    [FromQuery(Name = "from")]
    [Description("Only rows updated on or after this day: an ISO 8601 date such as 2026-01-31 (UTC).")]
    public string? From { get; set; }

    [FromQuery(Name = "to")]
    [Description("Only rows updated on or before this day, the whole day included: an ISO 8601 date such as 2026-01-31 (UTC).")]
    public string? To { get; set; }

    [FromQuery(Name = "sort")]
    [Description("Order by updated_at: 'asc' (oldest first) or 'desc' (newest first). Default desc.")]
    public string? Sort { get; set; }

    /// <summary>The request for the repository; throws an ArgumentException if a parameter is invalid.</summary>
    public PageRequest ToPageRequest()
    {
        var (request, errors) = Parse();
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors.Select(e => e.Message)));
        return request;
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        Parse().Errors.Select(e => new ValidationResult(e.Message, [e.Parameter]));

    private (PageRequest Request, List<(string Parameter, string Message)> Errors) Parse()
    {
        var errors = new List<(string Parameter, string Message)>();

        var page = Page ?? PageRequest.DefaultPage;
        if (page < 1)
            errors.Add(("page", "page must be 1 or greater."));

        var size = Size ?? PageRequest.DefaultSize;
        if (size is < 1 or > PageRequest.MaxSize)
            errors.Add(("size", $"size must be between 1 and {PageRequest.MaxSize}."));

        var from = ParseDate("from", From, errors);
        var to = ParseDate("to", To, errors);
        if (from > to)
            errors.Add(("from", "from must not be after to."));

        var sort = SortDirection.Desc;
        if (Sort is not null)
        {
            if (Sort.Equals("asc", StringComparison.OrdinalIgnoreCase))
                sort = SortDirection.Asc;
            else if (!Sort.Equals("desc", StringComparison.OrdinalIgnoreCase))
                errors.Add(("sort", "sort must be 'asc' or 'desc'."));
        }

        return (new PageRequest(page, size, from, to, sort), errors);
    }

    private static DateOnly? ParseDate(string parameter, string? value, List<(string, string)> errors)
    {
        if (value is null)
            return null;
        if (DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;
        errors.Add((parameter, $"{parameter} must be an ISO 8601 date such as 2026-01-31."));
        return null;
    }
}
