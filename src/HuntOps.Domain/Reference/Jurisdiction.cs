using HuntOps.Domain.Common;
using NodaTime;

namespace HuntOps.Domain.Reference;

/// <summary>A state, province, territory or other licensing authority area (e.g. a US state).</summary>
public sealed class Jurisdiction : Entity, IArchivable
{
    public required string Name { get; set; }

    /// <summary>Normalized <see cref="Name"/>; unique among active jurisdictions.</summary>
    public string NameKey { get; set; } = "";

    /// <summary>Short upper-case code ("KS", "WY"); unique among active jurisdictions.</summary>
    public required string Code { get; set; }

    /// <summary>ISO 3166-1 alpha-2 country code.</summary>
    public required string Country { get; set; }

    /// <summary>IANA zone used as the default for new events in this jurisdiction.</summary>
    public required string TimeZoneId { get; set; }

    public string? WebsiteUrl { get; set; }

    public string? Notes { get; set; }

    public Instant? ArchivedAt { get; set; }

    public List<Agency> Agencies { get; } = [];
}
