using HuntOps.Application.Abstractions;
using HuntOps.Application.Common;
using HuntOps.Application.Events;
using HuntOps.Domain.Common;
using HuntOps.Domain.Reference;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace HuntOps.Application.Reference;

public sealed record ProgramInput(
    Guid? AgencyId,
    string? Name,
    Guid? ParentProgramId = null,
    string? Species = null,
    string? Method = null,
    string? Residency = null,
    string? PermitType = null,
    string? Unit = null,
    string? Category = null,
    IReadOnlyDictionary<string, string>? Attributes = null,
    string? WebsiteUrl = null,
    string? Notes = null,
    uint? Version = null);

public sealed record ProgramDto(
    Guid Id,
    Guid AgencyId,
    string AgencyName,
    Guid JurisdictionId,
    string JurisdictionCode,
    Guid? ParentProgramId,
    string Name,
    string Slug,
    string? Species,
    string? Method,
    string? Residency,
    string? PermitType,
    string? Unit,
    string? Category,
    IReadOnlyDictionary<string, string> Attributes,
    string? WebsiteUrl,
    string? Notes,
    bool IsArchived,
    string? ArchivedAt,
    string CreatedAt,
    string UpdatedAt,
    uint Version)
{
    internal static ProgramDto From(HuntProgram p) => new(
        p.Id, p.AgencyId, p.Agency.Name, p.Agency.JurisdictionId, p.Agency.Jurisdiction.Code, p.ParentProgramId,
        p.Name, p.Slug, p.Species, p.Method, p.Residency, p.PermitType, p.Unit, p.Category,
        p.Attributes, p.WebsiteUrl, p.Notes,
        p.ArchivedAt is not null, TimeFormats.Format(p.ArchivedAt),
        TimeFormats.Format(p.CreatedAt), TimeFormats.Format(p.UpdatedAt), p.Version);
}

public sealed record ProgramFilter(
    Guid? JurisdictionId = null,
    Guid? AgencyId = null,
    string? Species = null,
    string? Search = null,
    bool IncludeArchived = false);

/// <summary>One season year of a program's history (architecture §4.3 program history view).</summary>
public sealed record ProgramSeasonDto(int SeasonYear, IReadOnlyList<ProgramHistoryEventDto> Events);

/// <summary>An event in a program's history. <see cref="InheritedFromProgramId"/> is set for umbrella-program events.</summary>
public sealed record ProgramHistoryEventDto(EventDto Event, Guid? InheritedFromProgramId);

public sealed class ProgramService(IHuntOpsDb db, IClock clock, IDateTimeZoneProvider zones)
{
    private const string Resource = "Program";
    private const int MaxAttributes = 50;

    public async Task<IReadOnlyList<ProgramDto>> ListAsync(ProgramFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Programs().AsNoTracking();
        if (filter.JurisdictionId is { } jid)
        {
            query = query.Where(p => p.Agency.JurisdictionId == jid);
        }

        if (filter.AgencyId is { } aid)
        {
            query = query.Where(p => p.AgencyId == aid);
        }

        if (!string.IsNullOrWhiteSpace(filter.Species))
        {
            var species = filter.Species.Trim().ToLowerInvariant();
            query = query.Where(p => p.Species != null && p.Species.ToLower() == species);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();
            query = query.Where(p => p.NameKey.Contains(term) || (p.Species != null && p.Species.ToLower().Contains(term)));
        }

        if (!filter.IncludeArchived)
        {
            query = query.Where(p => p.ArchivedAt == null);
        }

        var items = await query
            .OrderBy(p => p.Agency.Jurisdiction.Code)
            .ThenBy(p => p.Name)
            .ToListAsync(cancellationToken);
        return items.ConvertAll(ProgramDto.From);
    }

    public async Task<ProgramDto> GetAsync(Guid id, CancellationToken cancellationToken) =>
        ProgramDto.From(await LoadAsync(id, cancellationToken));

    public async Task<ProgramDto> CreateAsync(ProgramInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var program = new HuntProgram { Name = "", Slug = "" };
        await ApplyAsync(program, input, cancellationToken);
        program.Slug = await UniqueSlugAsync(program, cancellationToken);
        db.Programs.Add(program);
        await db.SaveChangesAsync(cancellationToken);
        return ProgramDto.From(program);
    }

    public async Task<ProgramDto> UpdateAsync(Guid id, ProgramInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var program = await LoadAsync(id, cancellationToken);
        await ApplyAsync(program, input, cancellationToken, requireVersion: true);
        await db.SaveChangesAsync(cancellationToken);
        return ProgramDto.From(program);
    }

    /// <summary>
    /// Archives a program. Its events and action history are kept (and remain visible in history),
    /// but they no longer appear in upcoming lists or action items.
    /// </summary>
    public async Task<ProgramDto> ArchiveAsync(Guid id, CancellationToken cancellationToken)
    {
        var program = await LoadAsync(id, cancellationToken);
        if (program.ArchivedAt is null)
        {
            var activeChildren = await db.Programs.CountAsync(p => p.ParentProgramId == id && p.ArchivedAt == null, cancellationToken);
            if (activeChildren > 0)
            {
                throw new ConflictException($"Program is the umbrella for {activeChildren} active program(s); archive or re-parent them first.");
            }

            program.ArchivedAt = clock.GetCurrentInstant();
            await db.SaveChangesAsync(cancellationToken);
        }

        return ProgramDto.From(program);
    }

    public async Task<ProgramDto> RestoreAsync(Guid id, CancellationToken cancellationToken)
    {
        var program = await LoadAsync(id, cancellationToken);
        if (program.ArchivedAt is not null)
        {
            program.Agency.EnsureActive("agencyId", "The program's agency");
            await EnsureUniqueAsync(program, program.NameKey, cancellationToken);
            if (await db.Programs.AnyAsync(p => p.ArchivedAt == null && p.Id != program.Id && p.Slug == program.Slug, cancellationToken))
            {
                program.Slug = await UniqueSlugAsync(program, cancellationToken);
            }

            program.ArchivedAt = null;
            await db.SaveChangesAsync(cancellationToken);
        }

        return ProgramDto.From(program);
    }

    /// <summary>
    /// Events for this program and its umbrella ancestors, grouped by season year (newest first),
    /// with each action's effective status. Archived events are excluded.
    /// </summary>
    public async Task<IReadOnlyList<ProgramSeasonDto>> GetHistoryAsync(Guid id, string userId, CancellationToken cancellationToken)
    {
        var program = await LoadAsync(id, cancellationToken);
        var ancestry = await AncestorIdsAsync(program, cancellationToken);
        var programIds = ancestry.Prepend(program.Id).ToList();

        var events = await db.ProgramEvents
            .AsNoTracking()
            .Include(e => e.Program).ThenInclude(p => p.Agency).ThenInclude(a => a.Jurisdiction)
            .Include(e => e.EventType)
            .Include(e => e.Actions)
            .Where(e => programIds.Contains(e.ProgramId) && e.ArchivedAt == null)
            .OrderByDescending(e => e.SeasonYear)
            .ThenBy(e => e.StartsAtUtc)
            .ToListAsync(cancellationToken);

        var latest = await ActionStatusLookup.LatestAsync(db, events.SelectMany(e => e.Actions).Select(a => a.Id), userId, cancellationToken);
        var now = clock.GetCurrentInstant();

        return events
            .GroupBy(e => e.SeasonYear)
            .Select(g => new ProgramSeasonDto(
                g.Key,
                g.Select(e => new ProgramHistoryEventDto(
                        EventMapper.ToDto(e, latest, now, includeArchivedActions: false),
                        e.ProgramId == program.Id ? null : e.ProgramId))
                    .ToList()))
            .ToList();
    }

    private IQueryable<HuntProgram> Programs() =>
        db.Programs.Include(p => p.Agency).ThenInclude(a => a.Jurisdiction);

    private async Task<HuntProgram> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        await Programs().FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
        ?? throw new NotFoundException(Resource, id);

    private async Task ApplyAsync(HuntProgram target, ProgramInput input, CancellationToken cancellationToken, bool requireVersion = false)
    {
        var v = new InputValidator(zones);
        var agencyId = v.RequiredId("agencyId", input.AgencyId);
        var name = v.RequiredText("name", input.Name);
        var species = v.OptionalText("species", input.Species);
        var method = v.OptionalText("method", input.Method);
        var residency = v.OptionalText("residency", input.Residency);
        var permitType = v.OptionalText("permitType", input.PermitType);
        var unit = v.OptionalText("unit", input.Unit);
        var category = v.OptionalText("category", input.Category);
        var website = v.OptionalUrl("websiteUrl", input.WebsiteUrl);
        var notes = v.OptionalText("notes", input.Notes, InputValidator.NotesMaxLength);
        var attributes = ValidateAttributes(v, input.Attributes);
        var version = requireVersion ? v.RequiredVersion(input.Version) : 0;
        v.ThrowIfInvalid();

        if (target.AgencyId != agencyId)
        {
            var agency = await db.Agencies.Include(a => a.Jurisdiction).FirstOrDefaultAsync(a => a.Id == agencyId, cancellationToken)
                ?? throw new RequestValidationException("agencyId", "Agency not found.");
            agency.EnsureActive("agencyId", "The agency");
            target.AgencyId = agency.Id;
            target.Agency = agency;
        }

        await ValidateParentAsync(target, input.ParentProgramId, cancellationToken);

        var nameKey = TextKeys.NameKey(name);
        await EnsureUniqueAsync(target, nameKey, cancellationToken);

        if (requireVersion)
        {
            db.ExpectVersion(target, version);
        }

        target.Name = name;
        target.NameKey = nameKey;
        target.ParentProgramId = input.ParentProgramId;
        target.Species = species;
        target.Method = method;
        target.Residency = residency;
        target.PermitType = permitType;
        target.Unit = unit;
        target.Category = category;
        target.Attributes = attributes;
        target.WebsiteUrl = website;
        target.Notes = notes;
    }

    private static Dictionary<string, string> ValidateAttributes(InputValidator v, IReadOnlyDictionary<string, string>? attributes)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (attributes is null)
        {
            return result;
        }

        if (attributes.Count > MaxAttributes)
        {
            v.Add("attributes", $"At most {MaxAttributes} attributes are allowed.");
            return result;
        }

        foreach (var (rawKey, rawValue) in attributes)
        {
            var key = rawKey?.Trim();
            if (string.IsNullOrEmpty(key) || key.Length > 64)
            {
                v.Add("attributes", "Attribute names must be 1-64 characters.");
                continue;
            }

            var value = rawValue?.Trim() ?? "";
            if (value.Length > 500)
            {
                v.Add("attributes", $"Attribute '{key}' must be at most 500 characters.");
                continue;
            }

            result[key] = value;
        }

        return result;
    }

    /// <summary>Umbrella programs must exist, be active, belong to the same agency and not create a cycle.</summary>
    private async Task ValidateParentAsync(HuntProgram target, Guid? parentId, CancellationToken cancellationToken)
    {
        if (parentId is null)
        {
            return;
        }

        if (parentId == target.Id)
        {
            throw new RequestValidationException("parentProgramId", "A program cannot be its own umbrella program.");
        }

        var parent = await db.Programs.FindAsync([parentId.Value], cancellationToken)
            ?? throw new RequestValidationException("parentProgramId", "Umbrella program not found.");
        parent.EnsureActive("parentProgramId", "The umbrella program");
        if (parent.AgencyId != target.AgencyId)
        {
            throw new RequestValidationException("parentProgramId", "The umbrella program must belong to the same agency.");
        }

        if ((await AncestorIdsAsync(parent, cancellationToken)).Contains(target.Id))
        {
            throw new RequestValidationException("parentProgramId", "That umbrella program would create a cycle.");
        }
    }

    private async Task<List<Guid>> AncestorIdsAsync(HuntProgram program, CancellationToken cancellationToken)
    {
        var ancestors = new List<Guid>();
        var parentId = program.ParentProgramId;
        while (parentId is { } current && !ancestors.Contains(current) && ancestors.Count < 20)
        {
            ancestors.Add(current);
            parentId = await db.Programs.Where(p => p.Id == current).Select(p => p.ParentProgramId).FirstOrDefaultAsync(cancellationToken);
        }

        return ancestors;
    }

    private async Task EnsureUniqueAsync(HuntProgram target, string nameKey, CancellationToken cancellationToken)
    {
        if (await db.Programs.AnyAsync(
                p => p.ArchivedAt == null && p.Id != target.Id && p.AgencyId == target.AgencyId && p.NameKey == nameKey,
                cancellationToken))
        {
            throw new ConflictException("An active program with the same name already exists for this agency.");
        }
    }

    private async Task<string> UniqueSlugAsync(HuntProgram program, CancellationToken cancellationToken)
    {
        var baseSlug = TextKeys.Slug($"{program.Agency.Jurisdiction.Code} {program.Name}", 90);
        if (baseSlug.Length == 0)
        {
            baseSlug = "program";
        }

        var taken = await db.Programs
            .Where(p => p.ArchivedAt == null && p.Id != program.Id && p.Slug.StartsWith(baseSlug))
            .Select(p => p.Slug)
            .ToListAsync(cancellationToken);

        var slug = baseSlug;
        for (var i = 2; taken.Contains(slug); i++)
        {
            slug = $"{baseSlug}-{i}";
        }

        return slug;
    }
}
