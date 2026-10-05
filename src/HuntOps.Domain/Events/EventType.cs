using System.Text.RegularExpressions;
using HuntOps.Domain.Common;
using NodaTime;

namespace HuntOps.Domain.Events;

public enum EventCategory
{
    Application,
    Purchase,
    Results,
    Reporting,
    Season,
    Other,
}

/// <summary>
/// Seeded, user-extensible lookup of event kinds ("application-period", "draw-results", ...).
/// The key is the stable identifier referenced by events; it is never changed or hard-deleted.
/// </summary>
public sealed partial class EventType : IAuditable, IArchivable
{
    public const int MaxKeyLength = 64;

    public required string Key { get; init; }

    public required string DisplayName { get; set; }

    /// <summary>Explains the type; later also given to the AI extractor as vocabulary.</summary>
    public string? Description { get; set; }

    public EventCategory Category { get; set; }

    public bool IsWindowByDefault { get; set; }

    public string? DefaultActionTitle { get; set; }

    /// <summary>True for types shipped with HuntOps (seeded by migration).</summary>
    public bool IsSystem { get; set; }

    public Instant CreatedAt { get; set; }

    public Instant UpdatedAt { get; set; }

    public Instant? ArchivedAt { get; set; }

    public uint Version { get; set; }

    /// <summary>Lowercase kebab-case: "application-period", "otc-sale".</summary>
    public static bool IsValidKey(string? key) =>
        key is { Length: > 0 and <= MaxKeyLength } && KeyPattern().IsMatch(key);

    public const string KeyRegex = "^[a-z0-9]+(-[a-z0-9]+)*$";

    [GeneratedRegex(KeyRegex, RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
