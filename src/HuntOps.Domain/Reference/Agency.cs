using HuntOps.Domain.Common;
using NodaTime;

namespace HuntOps.Domain.Reference;

/// <summary>A wildlife agency that runs licensing/draw programs within a jurisdiction.</summary>
public sealed class Agency : Entity, IArchivable
{
    public Guid JurisdictionId { get; set; }

    public Jurisdiction Jurisdiction { get; set; } = null!;

    public required string Name { get; set; }

    /// <summary>Normalized <see cref="Name"/>; unique per jurisdiction among active agencies.</summary>
    public string NameKey { get; set; } = "";

    public string? Abbreviation { get; set; }

    public string? WebsiteUrl { get; set; }

    public string? Notes { get; set; }

    public Instant? ArchivedAt { get; set; }

    public List<HuntProgram> Programs { get; } = [];
}
