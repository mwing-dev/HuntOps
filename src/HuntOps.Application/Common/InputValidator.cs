using NodaTime;

namespace HuntOps.Application.Common;

/// <summary>
/// Collects field-level errors while normalizing input. Every check returns the cleaned value
/// (or null) so services read as "validate, then use" without a separate pass.
/// </summary>
public sealed class InputValidator(IDateTimeZoneProvider zones)
{
    public const int NameMaxLength = 200;
    public const int ShortTextMaxLength = 100;
    public const int NotesMaxLength = 4000;
    public const int UrlMaxLength = 2000;

    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool IsValid => _errors.Count == 0;

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list))
        {
            _errors[field] = list = [];
        }

        list.Add(message);
    }

    public void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new RequestValidationException(_errors.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal));
        }
    }

    /// <summary>Trimmed, non-empty, at most <paramref name="maxLength"/> characters.</summary>
    public string RequiredText(string field, string? value, int maxLength = NameMaxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            Add(field, "This field is required.");
            return "";
        }

        CheckLength(field, trimmed, maxLength);
        return trimmed;
    }

    /// <summary>Trimmed; empty becomes null.</summary>
    public string? OptionalText(string field, string? value, int maxLength = ShortTextMaxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        CheckLength(field, trimmed, maxLength);
        return trimmed;
    }

    /// <summary>Absolute http/https URL only (no javascript:, file:, data:, ...).</summary>
    public string? OptionalUrl(string field, string? value)
    {
        var trimmed = OptionalText(field, value, UrlMaxLength);
        if (trimmed is null)
        {
            return null;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(uri.Host))
        {
            Add(field, "Must be an absolute http:// or https:// URL.");
            return null;
        }

        return trimmed;
    }

    public LocalDate? Date(string field, string? value, bool required)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                Add(field, "This field is required.");
            }

            return null;
        }

        if (TimeFormats.TryParseDate(value, out var date))
        {
            return date;
        }

        Add(field, $"'{Truncate(value)}' is not a valid date; use {TimeFormats.DateFormat} (e.g. 2027-06-12).");
        return null;
    }

    public LocalTime? Time(string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (TimeFormats.TryParseTime(value, out var time))
        {
            return time;
        }

        Add(field, $"'{Truncate(value)}' is not a valid time; use 24-hour {TimeFormats.TimeFormat} (e.g. 17:00).");
        return null;
    }

    /// <summary>A known IANA zone id. Returns null when absent (and not required) or invalid.</summary>
    public string? TimeZone(string field, string? value, bool required)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            if (required)
            {
                Add(field, "This field is required.");
            }

            return null;
        }

        if (zones.GetZoneOrNull(trimmed) is null)
        {
            Add(field, $"'{Truncate(trimmed)}' is not a known IANA time zone (e.g. America/Chicago).");
            return null;
        }

        return trimmed;
    }

    public int? IntRange(string field, int? value, int min, int max, bool required)
    {
        if (value is null)
        {
            if (required)
            {
                Add(field, "This field is required.");
            }

            return null;
        }

        if (value < min || value > max)
        {
            Add(field, $"Must be between {min} and {max}.");
            return null;
        }

        return value;
    }

    /// <summary>Case-insensitive enum name. Numeric strings are rejected.</summary>
    public TEnum? Enum<TEnum>(string field, string? value, bool required)
        where TEnum : struct, Enum
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            if (required)
            {
                Add(field, "This field is required.");
            }

            return null;
        }

        if (!trimmed.All(char.IsDigit) && System.Enum.TryParse<TEnum>(trimmed, ignoreCase: true, out var parsed) && System.Enum.IsDefined(parsed))
        {
            return parsed;
        }

        var allowed = string.Join(", ", System.Enum.GetNames<TEnum>().Select(ToCamelCase));
        Add(field, $"'{Truncate(trimmed)}' is not valid. Allowed values: {allowed}.");
        return null;
    }

    public Guid RequiredId(string field, Guid? value)
    {
        if (value is null || value == Guid.Empty)
        {
            Add(field, "This field is required.");
            return Guid.Empty;
        }

        return value.Value;
    }

    public uint RequiredVersion(uint? value)
    {
        if (value is null)
        {
            Add("version", "The current version is required for updates (read it from the resource first).");
            return 0;
        }

        return value.Value;
    }

    public static string ToCamelCase(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];

    private void CheckLength(string field, string value, int maxLength)
    {
        if (value.Length > maxLength)
        {
            Add(field, $"Must be at most {maxLength} characters.");
        }
    }

    // Echoed input is truncated so error messages stay small.
    private static string Truncate(string value) => value.Length <= 40 ? value : value[..40] + "…";
}
