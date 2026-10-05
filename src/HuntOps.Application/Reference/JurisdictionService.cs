using HuntOps.Application.Abstractions;
using HuntOps.Application.Common;
using HuntOps.Domain.Common;
using HuntOps.Domain.Reference;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace HuntOps.Application.Reference;

public sealed record JurisdictionInput(
    string? Name,
    string? Code,
    string? Country,
    string? TimeZoneId,
    string? WebsiteUrl = null,
    string? Notes = null,
    uint? Version = null);

public sealed record JurisdictionDto(
    Guid Id,
    string Name,
    string Code,
    string Country,
    string TimeZoneId,
    string? WebsiteUrl,
    string? Notes,
    bool IsArchived,
    string? ArchivedAt,
    string CreatedAt,
    string UpdatedAt,
    uint Version)
{
    internal static JurisdictionDto From(Jurisdiction j) => new(
        j.Id, j.Name, j.Code, j.Country, j.TimeZoneId, j.WebsiteUrl, j.Notes,
        j.ArchivedAt is not null, TimeFormats.Format(j.ArchivedAt),
        TimeFormats.Format(j.CreatedAt), TimeFormats.Format(j.UpdatedAt), j.Version);
}

public sealed class JurisdictionService(IHuntOpsDb db, IClock clock, IDateTimeZoneProvider zones)
{
    private const string Resource = "Jurisdiction";

    public async Task<IReadOnlyList<JurisdictionDto>> ListAsync(bool includeArchived, CancellationToken cancellationToken)
    {
        var query = db.Jurisdictions.AsNoTracking();
        if (!includeArchived)
        {
            query = query.Where(j => j.ArchivedAt == null);
        }

        var items = await query.OrderBy(j => j.Name).ToListAsync(cancellationToken);
        return items.ConvertAll(JurisdictionDto.From);
    }

    public async Task<JurisdictionDto> GetAsync(Guid id, CancellationToken cancellationToken) =>
        JurisdictionDto.From(await db.Jurisdictions.FindOrThrowAsync(id, Resource, cancellationToken));

    public async Task<JurisdictionDto> CreateAsync(JurisdictionInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var jurisdiction = new Jurisdiction { Name = "", Code = "", Country = "", TimeZoneId = "" };
        await ApplyAsync(jurisdiction, input, cancellationToken);
        db.Jurisdictions.Add(jurisdiction);
        await db.SaveChangesAsync(cancellationToken);
        return JurisdictionDto.From(jurisdiction);
    }

    public async Task<JurisdictionDto> UpdateAsync(Guid id, JurisdictionInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var jurisdiction = await db.Jurisdictions.FindOrThrowAsync(id, Resource, cancellationToken);
        await ApplyAsync(jurisdiction, input, cancellationToken, requireVersion: true);
        await db.SaveChangesAsync(cancellationToken);
        return JurisdictionDto.From(jurisdiction);
    }

    /// <summary>Archives a jurisdiction. Refused while it still has active agencies, so nothing is orphaned.</summary>
    public async Task<JurisdictionDto> ArchiveAsync(Guid id, CancellationToken cancellationToken)
    {
        var jurisdiction = await db.Jurisdictions.FindOrThrowAsync(id, Resource, cancellationToken);
        if (jurisdiction.ArchivedAt is null)
        {
            var activeAgencies = await db.Agencies.CountAsync(a => a.JurisdictionId == id && a.ArchivedAt == null, cancellationToken);
            if (activeAgencies > 0)
            {
                throw new ConflictException($"Jurisdiction has {activeAgencies} active agenc{(activeAgencies == 1 ? "y" : "ies")}; archive them first.");
            }

            jurisdiction.ArchivedAt = clock.GetCurrentInstant();
            await db.SaveChangesAsync(cancellationToken);
        }

        return JurisdictionDto.From(jurisdiction);
    }

    public async Task<JurisdictionDto> RestoreAsync(Guid id, CancellationToken cancellationToken)
    {
        var jurisdiction = await db.Jurisdictions.FindOrThrowAsync(id, Resource, cancellationToken);
        if (jurisdiction.ArchivedAt is not null)
        {
            await EnsureUniqueAsync(jurisdiction.Id, jurisdiction.Code, jurisdiction.NameKey, cancellationToken);
            jurisdiction.ArchivedAt = null;
            await db.SaveChangesAsync(cancellationToken);
        }

        return JurisdictionDto.From(jurisdiction);
    }

    private async Task ApplyAsync(Jurisdiction target, JurisdictionInput input, CancellationToken cancellationToken, bool requireVersion = false)
    {
        var v = new InputValidator(zones);
        var name = v.RequiredText("name", input.Name);
        var code = TextKeys.Code(v.RequiredText("code", input.Code, 10));
        if (code.Length > 0 && !code.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
        {
            v.Add("code", "Use letters, digits and hyphens only (e.g. KS, WY, BC).");
        }

        var country = TextKeys.Code(v.RequiredText("country", input.Country, 2));
        if (country.Length > 0 && (country.Length != 2 || !country.All(char.IsAsciiLetter)))
        {
            v.Add("country", "Use a two-letter ISO 3166-1 country code (e.g. US, CA).");
        }

        var timeZone = v.TimeZone("timeZoneId", input.TimeZoneId, required: true);
        var website = v.OptionalUrl("websiteUrl", input.WebsiteUrl);
        var notes = v.OptionalText("notes", input.Notes, InputValidator.NotesMaxLength);
        var version = requireVersion ? v.RequiredVersion(input.Version) : 0;
        v.ThrowIfInvalid();

        var nameKey = TextKeys.NameKey(name);
        await EnsureUniqueAsync(target.Id, code, nameKey, cancellationToken);

        if (requireVersion)
        {
            db.ExpectVersion(target, version);
        }

        target.Name = name;
        target.NameKey = nameKey;
        target.Code = code;
        target.Country = country;
        target.TimeZoneId = timeZone!;
        target.WebsiteUrl = website;
        target.Notes = notes;
    }

    private async Task EnsureUniqueAsync(Guid id, string code, string nameKey, CancellationToken cancellationToken)
    {
        var clash = await db.Jurisdictions
            .Where(j => j.ArchivedAt == null && j.Id != id && (j.Code == code || j.NameKey == nameKey))
            .Select(j => new { j.Code, j.NameKey })
            .FirstOrDefaultAsync(cancellationToken);

        if (clash is not null)
        {
            throw new ConflictException(clash.Code == code
                ? $"An active jurisdiction with code '{code}' already exists."
                : "An active jurisdiction with the same name already exists.");
        }
    }
}
