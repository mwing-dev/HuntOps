using HuntOps.Application.Abstractions;
using HuntOps.Application.Common;
using HuntOps.Domain.Common;
using HuntOps.Domain.Reference;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace HuntOps.Application.Reference;

public sealed record AgencyInput(
    Guid? JurisdictionId,
    string? Name,
    string? Abbreviation = null,
    string? WebsiteUrl = null,
    string? Notes = null,
    uint? Version = null);

public sealed record AgencyDto(
    Guid Id,
    Guid JurisdictionId,
    string JurisdictionCode,
    string Name,
    string? Abbreviation,
    string? WebsiteUrl,
    string? Notes,
    bool IsArchived,
    string? ArchivedAt,
    string CreatedAt,
    string UpdatedAt,
    uint Version)
{
    internal static AgencyDto From(Agency a) => new(
        a.Id, a.JurisdictionId, a.Jurisdiction.Code, a.Name, a.Abbreviation, a.WebsiteUrl, a.Notes,
        a.ArchivedAt is not null, TimeFormats.Format(a.ArchivedAt),
        TimeFormats.Format(a.CreatedAt), TimeFormats.Format(a.UpdatedAt), a.Version);
}

public sealed class AgencyService(IHuntOpsDb db, IClock clock, IDateTimeZoneProvider zones)
{
    private const string Resource = "Agency";

    public async Task<IReadOnlyList<AgencyDto>> ListAsync(Guid? jurisdictionId, bool includeArchived, CancellationToken cancellationToken)
    {
        var query = db.Agencies.AsNoTracking().Include(a => a.Jurisdiction).AsQueryable();
        if (jurisdictionId is { } jid)
        {
            query = query.Where(a => a.JurisdictionId == jid);
        }

        if (!includeArchived)
        {
            query = query.Where(a => a.ArchivedAt == null);
        }

        var items = await query.OrderBy(a => a.Jurisdiction.Code).ThenBy(a => a.Name).ToListAsync(cancellationToken);
        return items.ConvertAll(AgencyDto.From);
    }

    public async Task<AgencyDto> GetAsync(Guid id, CancellationToken cancellationToken) =>
        AgencyDto.From(await LoadAsync(id, cancellationToken));

    public async Task<AgencyDto> CreateAsync(AgencyInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var agency = new Agency { Name = "" };
        await ApplyAsync(agency, input, cancellationToken);
        db.Agencies.Add(agency);
        await db.SaveChangesAsync(cancellationToken);
        return AgencyDto.From(agency);
    }

    public async Task<AgencyDto> UpdateAsync(Guid id, AgencyInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var agency = await LoadAsync(id, cancellationToken);
        await ApplyAsync(agency, input, cancellationToken, requireVersion: true);
        await db.SaveChangesAsync(cancellationToken);
        return AgencyDto.From(agency);
    }

    /// <summary>Refused while the agency still has active programs.</summary>
    public async Task<AgencyDto> ArchiveAsync(Guid id, CancellationToken cancellationToken)
    {
        var agency = await LoadAsync(id, cancellationToken);
        if (agency.ArchivedAt is null)
        {
            var activePrograms = await db.Programs.CountAsync(p => p.AgencyId == id && p.ArchivedAt == null, cancellationToken);
            if (activePrograms > 0)
            {
                throw new ConflictException($"Agency has {activePrograms} active program(s); archive them first.");
            }

            agency.ArchivedAt = clock.GetCurrentInstant();
            await db.SaveChangesAsync(cancellationToken);
        }

        return AgencyDto.From(agency);
    }

    public async Task<AgencyDto> RestoreAsync(Guid id, CancellationToken cancellationToken)
    {
        var agency = await LoadAsync(id, cancellationToken);
        if (agency.ArchivedAt is not null)
        {
            agency.Jurisdiction.EnsureActive("jurisdictionId", "The agency's jurisdiction");
            await EnsureUniqueAsync(agency.Id, agency.JurisdictionId, agency.NameKey, cancellationToken);
            agency.ArchivedAt = null;
            await db.SaveChangesAsync(cancellationToken);
        }

        return AgencyDto.From(agency);
    }

    private async Task<Agency> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Agencies.Include(a => a.Jurisdiction).FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
        ?? throw new NotFoundException(Resource, id);

    private async Task ApplyAsync(Agency target, AgencyInput input, CancellationToken cancellationToken, bool requireVersion = false)
    {
        var v = new InputValidator(zones);
        var jurisdictionId = v.RequiredId("jurisdictionId", input.JurisdictionId);
        var name = v.RequiredText("name", input.Name);
        var abbreviation = v.OptionalText("abbreviation", input.Abbreviation, 20);
        var website = v.OptionalUrl("websiteUrl", input.WebsiteUrl);
        var notes = v.OptionalText("notes", input.Notes, InputValidator.NotesMaxLength);
        var version = requireVersion ? v.RequiredVersion(input.Version) : 0;
        v.ThrowIfInvalid();

        if (target.JurisdictionId != jurisdictionId)
        {
            var jurisdiction = await db.Jurisdictions.FindAsync([jurisdictionId], cancellationToken)
                ?? throw new RequestValidationException("jurisdictionId", "Jurisdiction not found.");
            jurisdiction.EnsureActive("jurisdictionId", "The jurisdiction");
            target.JurisdictionId = jurisdictionId;
            target.Jurisdiction = jurisdiction;
        }

        var nameKey = TextKeys.NameKey(name);
        await EnsureUniqueAsync(target.Id, jurisdictionId, nameKey, cancellationToken);

        if (requireVersion)
        {
            db.ExpectVersion(target, version);
        }

        target.Name = name;
        target.NameKey = nameKey;
        target.Abbreviation = abbreviation;
        target.WebsiteUrl = website;
        target.Notes = notes;
    }

    private async Task EnsureUniqueAsync(Guid id, Guid jurisdictionId, string nameKey, CancellationToken cancellationToken)
    {
        if (await db.Agencies.AnyAsync(
                a => a.ArchivedAt == null && a.Id != id && a.JurisdictionId == jurisdictionId && a.NameKey == nameKey,
                cancellationToken))
        {
            throw new ConflictException("An active agency with the same name already exists in this jurisdiction.");
        }
    }
}
