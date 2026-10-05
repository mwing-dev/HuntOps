using NodaTime;
using NodaTime.Text;

namespace HuntOps.Application.Common;

/// <summary>
/// Wire formats for temporal values. The API (and later MCP) exchanges ISO strings, and all parsing happens here
/// so callers get field-level validation messages instead of serializer failures.
/// </summary>
public static class TimeFormats
{
    public const string DateFormat = "yyyy-MM-dd";
    public const string TimeFormat = "HH:mm";

    private static readonly LocalDatePattern DatePattern = LocalDatePattern.Iso;
    private static readonly LocalTimePattern ShortTimePattern = LocalTimePattern.CreateWithInvariantCulture("HH:mm");
    private static readonly LocalTimePattern LongTimePattern = LocalTimePattern.CreateWithInvariantCulture("HH:mm:ss");
    private static readonly InstantPattern InstantFormat = InstantPattern.ExtendedIso;

    public static bool TryParseDate(string? text, out LocalDate date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var result = DatePattern.Parse(text.Trim());
        date = result.Success ? result.Value : default;
        return result.Success;
    }

    /// <summary>Accepts "HH:mm" or "HH:mm:ss" (24-hour).</summary>
    public static bool TryParseTime(string? text, out LocalTime time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        var result = ShortTimePattern.Parse(trimmed);
        if (!result.Success)
        {
            result = LongTimePattern.Parse(trimmed);
        }

        time = result.Success ? result.Value : default;
        return result.Success;
    }

    public static bool TryParseInstant(string? text, out Instant instant)
    {
        instant = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var result = InstantFormat.Parse(text.Trim());
        instant = result.Success ? result.Value : default;
        return result.Success;
    }

    public static string Format(LocalDate date) => DatePattern.Format(date);

    public static string? Format(LocalDate? date) => date is { } value ? DatePattern.Format(value) : null;

    public static string? Format(LocalTime? time) =>
        time is not { } value ? null
        : value.Second == 0 && value.TickOfSecond == 0 ? ShortTimePattern.Format(value)
        : LongTimePattern.Format(value);

    public static string Format(Instant instant) => InstantFormat.Format(instant);

    public static string? Format(Instant? instant) => instant is { } value ? InstantFormat.Format(value) : null;
}
