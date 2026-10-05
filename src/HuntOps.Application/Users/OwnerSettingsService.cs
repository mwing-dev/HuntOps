using HuntOps.Application.Abstractions;
using HuntOps.Application.Common;
using HuntOps.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NodaTime;

namespace HuntOps.Application.Users;

/// <summary>Resolves the single V1 owner account (an Identity user flagged as owner).</summary>
public interface IOwnerDirectory
{
    /// <summary>The owner's Identity user id, or null when no owner has been bootstrapped yet.</summary>
    Task<string?> GetOwnerUserIdAsync(CancellationToken cancellationToken);
}

public sealed class OwnerDefaultsOptions
{
    /// <summary>Default owner time zone for new installs (HUNTOPS_OWNER_TIMEZONE).</summary>
    public string TimeZoneId { get; set; } = OwnerSettings.DefaultTimeZoneId;
}

public sealed record OwnerSettingsDto(
    string TimeZoneId,
    bool QuietHoursEnabled,
    string QuietHoursStart,
    string QuietHoursEnd,
    bool UrgentBypassesQuietHours,
    bool IsSaved,
    uint Version);

public sealed record OwnerSettingsInput(
    string? TimeZoneId,
    bool? QuietHoursEnabled,
    string? QuietHoursStart,
    string? QuietHoursEnd,
    bool? UrgentBypassesQuietHours,
    uint? Version = null);

/// <summary>
/// Owner preferences. Read by the dashboard for time display now, and by reminder scheduling in Phase 4.
/// Settings that were never saved are reported with defaults (<see cref="OwnerSettingsDto.IsSaved"/> = false).
/// </summary>
public sealed class OwnerSettingsService(
    IHuntOpsDb db,
    IDateTimeZoneProvider zones,
    ICurrentActor actor,
    IOptions<OwnerDefaultsOptions> defaults)
{
    public async Task<OwnerSettingsDto> GetAsync(CancellationToken cancellationToken)
    {
        var settings = await db.OwnerSettings.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == actor.UserId, cancellationToken);
        return settings is null ? ToDto(CreateDefaults(actor.UserId), isSaved: false) : ToDto(settings, isSaved: true);
    }

    /// <summary>The owner's IANA zone (or the configured default).</summary>
    public async Task<string> GetTimeZoneIdAsync(CancellationToken cancellationToken) =>
        (await GetAsync(cancellationToken)).TimeZoneId;

    public async Task<OwnerSettingsDto> UpdateAsync(OwnerSettingsInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (actor.UserId == Owner.PlaceholderUserId)
        {
            throw new ConflictException("Owner settings can only be saved once an owner account exists.");
        }

        var v = new InputValidator(zones);
        var timeZone = v.TimeZone("timeZoneId", input.TimeZoneId, required: true);
        var start = v.Time("quietHoursStart", input.QuietHoursStart);
        var end = v.Time("quietHoursEnd", input.QuietHoursEnd);
        if (start is null && !v.HasError("quietHoursStart"))
        {
            v.Add("quietHoursStart", "This field is required.");
        }

        if (end is null && !v.HasError("quietHoursEnd"))
        {
            v.Add("quietHoursEnd", "This field is required.");
        }

        if (start is not null && start == end)
        {
            v.Add("quietHoursEnd", "Quiet hours must start and end at different times.");
        }

        var settings = await db.OwnerSettings.FirstOrDefaultAsync(s => s.UserId == actor.UserId, cancellationToken);
        if (settings is not null)
        {
            var version = v.RequiredVersion(input.Version);
            v.ThrowIfInvalid();
            db.ExpectVersion(settings, version);
        }
        else
        {
            v.ThrowIfInvalid();
            settings = CreateDefaults(actor.UserId);
            db.OwnerSettings.Add(settings);
        }

        settings.TimeZoneId = timeZone!;
        settings.QuietHoursEnabled = input.QuietHoursEnabled ?? true;
        settings.QuietHoursStart = start!.Value;
        settings.QuietHoursEnd = end!.Value;
        settings.UrgentBypassesQuietHours = input.UrgentBypassesQuietHours ?? true;
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(settings, isSaved: true);
    }

    internal OwnerSettings CreateDefaults(string userId) => new()
    {
        UserId = userId,
        TimeZoneId = zones.GetZoneOrNull(defaults.Value.TimeZoneId) is null ? OwnerSettings.DefaultTimeZoneId : defaults.Value.TimeZoneId,
    };

    private static OwnerSettingsDto ToDto(OwnerSettings s, bool isSaved) => new(
        s.TimeZoneId, s.QuietHoursEnabled, TimeFormats.Format(s.QuietHoursStart)!, TimeFormats.Format(s.QuietHoursEnd)!,
        s.UrgentBypassesQuietHours, isSaved, s.Version);
}
