using System.Text.RegularExpressions;

namespace CargoHub.Api.Validation;

// Reusable input checks for text fields. The Check* methods throw an ArgumentException
// (the controllers turn that into a 400); the Is* methods only return true or false,
// so a validator can collect several errors into one message.
public static partial class FieldRules
{
    public const int MaxTextLength = 255;

    // RFC 5321 limits: 254 characters in total, 64 for the part before the @.
    private const int MaxEmailLength = 254;
    private const int MaxEmailLocalPartLength = 64;

    // E.164: a phone number has at most 15 digits; shorter than 9 is never a full number.
    public const int MinPhoneDigits = 9;
    public const int MaxPhoneDigits = 15;

    public const string EmailError = "must be a valid email address such as 'name@example.com'.";
    public static readonly string PhoneError =
        $"must be a phone number of {MinPhoneDigits} to {MaxPhoneDigits} digits, optionally starting with '+' " +
        "and using only spaces, hyphens, periods or brackets as separators.";
    public const string DutchZipCodeError = "must be a Dutch postal code such as '1234 AB' or '1234AB'.";
    public const string ZipCodeError = "must be 2 to 10 letters or digits, optionally separated by a space or hyphen.";
    public const string NameError =
        "may only contain letters, spaces, hyphens, apostrophes and periods, and must start with a letter.";

    // Returns the trimmed value, or an empty string for null.
    public static string Trim(string? value) => value?.Trim() ?? string.Empty;

    // Throws if any field is empty.
    public static void CheckRequired(params (string Name, string Value)[] fields)
    {
        var missing = fields.Where(f => string.IsNullOrWhiteSpace(f.Value)).Select(f => f.Name).ToList();
        if (missing.Count > 0)
            throw new ArgumentException($"Missing required fields: {string.Join(", ", missing)}.");
    }

    // Throws if any field is longer than the database column allows.
    public static void CheckMaxLength(params (string Name, string Value)[] fields)
    {
        var tooLong = fields.Where(f => f.Value.Length > MaxTextLength).Select(f => f.Name).ToList();
        if (tooLong.Count > 0)
            throw new ArgumentException(
                $"Fields must not exceed {MaxTextLength} characters: {string.Join(", ", tooLong)}.");
    }

    // Throws if any field contains line breaks, tabs or other control characters.
    public static void CheckNoControlCharacters(params (string Name, string Value)[] fields)
    {
        var invalid = fields.Where(f => f.Value.Any(char.IsControl)).Select(f => f.Name).ToList();
        if (invalid.Count > 0)
            throw new ArgumentException(
                $"Fields must not contain control characters such as line breaks or tabs: {string.Join(", ", invalid)}.");
    }

    // Throws with all collected errors in one message, if there are any.
    public static void ThrowIfAny(List<string> errors)
    {
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors));
    }

    // Returns true for an address in the form local-part@domain.tld.
    public static bool IsValidEmail(string email)
    {
        if (email.Length > MaxEmailLength || !Email().IsMatch(email))
            return false;
        return email.IndexOf('@') <= MaxEmailLocalPartLength;
    }

    // Returns true for a phone number such as "+31 (0)10 123 4567", "010-1234567" or "0612345678".
    public static bool IsValidPhone(string phone)
    {
        if (!PhoneCharacters().IsMatch(phone))
            return false;

        // Brackets must be balanced and not nested, e.g. "(010)" or "+31 (0)10".
        var depth = 0;
        foreach (var c in phone)
        {
            if (c == '(' && ++depth > 1) return false;
            if (c == ')' && --depth < 0) return false;
        }
        if (depth != 0)
            return false;

        var digits = phone.Count(char.IsAsciiDigit);
        return digits is >= MinPhoneDigits and <= MaxPhoneDigits;
    }

    // Checks the Dutch format when the country is the Netherlands, otherwise a general format.
    public static bool IsValidZipCode(string zipCode, string country) =>
        IsNetherlands(country) ? DutchZipCode().IsMatch(zipCode) : GenericZipCode().IsMatch(zipCode);

    // Returns the error text that fits the zip code rule for this country.
    public static string ZipCodeErrorFor(string country) =>
        IsNetherlands(country) ? DutchZipCodeError : ZipCodeError;

    // Returns true for a city, province, country or person name such as "'s-Hertogenbosch" or "J. de Vries".
    public static bool IsPlaceOrPersonName(string value) => PlaceOrPersonName().IsMatch(value);

    public static bool IsNetherlands(string country) =>
        country.Equals("Netherlands", StringComparison.OrdinalIgnoreCase) ||
        country.Equals("The Netherlands", StringComparison.OrdinalIgnoreCase) ||
        country.Equals("Nederland", StringComparison.OrdinalIgnoreCase) ||
        country.Equals("NL", StringComparison.OrdinalIgnoreCase);

    // Letters (incl. accents), spaces, hyphens, apostrophes and periods, starting with a letter or with an
    // apostrophe directly followed by a letter, e.g. "Den Haag", "'s-Hertogenbosch", "Nieuw- en Sint Joosland", "J. de Vries".
    [GeneratedRegex(@"^['’]?\p{L}[\p{L}\p{M} '’.\-]*$")]
    private static partial Regex PlaceOrPersonName();

    // 1234 AB or 1234AB; the first digit is never 0. Letters may be lowercase.
    [GeneratedRegex(@"^[1-9][0-9]{3} ?[A-Za-z]{2}$")]
    private static partial Regex DutchZipCode();

    [GeneratedRegex(@"^[A-Za-z0-9](?:[A-Za-z0-9]|[ -](?=[A-Za-z0-9])){1,9}$")]
    private static partial Regex GenericZipCode();

    // Optional leading +, then digits with spaces, hyphens, periods or brackets in between.
    [GeneratedRegex(@"^\+?[0-9() .\-]+$")]
    private static partial Regex PhoneCharacters();

    // Local part: dot-separated atoms (no leading, trailing or double dots).
    // Domain: dot-separated labels that do not start or end with a hyphen, ending in a TLD of 2+ letters.
    [GeneratedRegex(@"^[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+(?:\.[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+)*@(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z]{2,63}$")]
    private static partial Regex Email();
}