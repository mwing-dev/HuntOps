using HuntOps.Domain.Common;
using NodaTime;

namespace HuntOps.Domain.Reference;

/// <summary>
/// What a deadline relates to: a draw, permit, license, point system or season offering
/// (exposed as "program" in the API). All categorization fields are free text so no state, species
/// or permit system is baked into the schema. (Named HuntProgram to avoid clashing with host Program classes.)
/// </summary>
public sealed class HuntProgram : Entity, IArchivable
{
    public Guid AgencyId { get; set; }

    public Agency Agency { get; set; } = null!;

    /// <summary>Optional umbrella program (same agency) whose events children inherit, e.g. a statewide primary draw.</summary>
    public Guid? ParentProgramId { get; set; }

    public HuntProgram? ParentProgram { get; set; }

    public required string Name { get; set; }

    /// <summary>Normalized <see cref="Name"/>; unique per agency among active programs.</summary>
    public string NameKey { get; set; } = "";

    /// <summary>URL-friendly identifier, unique among active programs.</summary>
    public required string Slug { get; set; }

    public string? Species { get; set; }

    public string? Method { get; set; }

    public string? Residency { get; set; }

    public string? PermitType { get; set; }

    public string? Unit { get; set; }

    public string? Category { get; set; }

    /// <summary>Arbitrary extra key/value attributes (hunt code, bag type, ...).</summary>
    public Dictionary<string, string> Attributes { get; set; } = [];

    public string? WebsiteUrl { get; set; }

    public string? Notes { get; set; }

    public Instant? ArchivedAt { get; set; }
}
