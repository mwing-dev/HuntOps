using HuntOps.Application.Abstractions;
using HuntOps.Application.Common;
using HuntOps.Domain.Events;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace HuntOps.Application.Reference;

public sealed record EventTypeInput(
    string? Key,
    string? DisplayName,
    string? Category,
    string? Description = null,
    bool? IsWindowByDefault = null,
    string? DefaultActionTitle = null,
    uint? Version = null);

public sealed record EventTypeDto(
    string Key,
    string DisplayName,
    string? Description,
    EventCategory Category,
    bool IsWindowByDefault,
    string? DefaultActionTitle,
    bool IsSystem,
    bool IsArchived,
    uint Version)
{
    internal static EventTypeDto From(EventType t) => new(
        t.Key, t.DisplayName, t.Description, t.Category, t.IsWindowByDefault, t.DefaultActionTitle,
        t.IsSystem, t.ArchivedAt is not null, t.Version);
}

/// <summary>Manages the event-type vocabulary. Keys are permanent; types are archived, never deleted.</summary>
public sealed class EventTypeService(IHuntOpsDb db, IClock clock, IDateTimeZoneProvider zones)
{
    private const string Resource = "Event type";

    public async Task<IReadOnlyList<EventTypeDto>> ListAsync(bool includeArchived, CancellationToken cancellationToken)
    {
        var query = db.EventTypes.AsNoTracking();
        if (!includeArchived)
        {
            query = query.Where(t => t.ArchivedAt == null);
        }

        var items = await query.OrderBy(t => t.Category).ThenBy(t => t.DisplayName).ToListAsync(cancellationToken);
        return items.ConvertAll(EventTypeDto.From);
    }

    public async Task<EventTypeDto> GetAsync(string key, CancellationToken cancellationToken) =>
        EventTypeDto.From(await db.EventTypes.FindOrThrowAsync(key, Resource, cancellationToken));

    public async Task<EventTypeDto> CreateAsync(EventTypeInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var v = new InputValidator(zones);
        var key = v.RequiredText("key", input.Key, EventType.MaxKeyLength);
        if (key.Length > 0 && !EventType.IsValidKey(key))
        {
            v.Add("key", "Use lowercase letters and digits separated by single hyphens (e.g. 'leftover-sale').");
        }

        var fields = ValidateFields(v, input);
        v.ThrowIfInvalid();

        if (await db.EventTypes.AnyAsync(t => t.Key == key, cancellationToken))
        {
            throw new ConflictException($"Event type '{key}' already exists (it may be archived; restore it instead).");
        }

        var eventType = new EventType { Key = key, DisplayName = fields.DisplayName, IsSystem = false };
        fields.ApplyTo(eventType);
        db.EventTypes.Add(eventType);
        await db.SaveChangesAsync(cancellationToken);
        return EventTypeDto.From(eventType);
    }

    /// <summary>Updates display fields. The key cannot change because events reference it.</summary>
    public async Task<EventTypeDto> UpdateAsync(string key, EventTypeInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var eventType = await db.EventTypes.FindOrThrowAsync(key, Resource, cancellationToken);
        var v = new InputValidator(zones);
        if (input.Key is not null && !string.Equals(input.Key.Trim(), key, StringComparison.Ordinal))
        {
            v.Add("key", "The key of an event type cannot be changed.");
        }

        var fields = ValidateFields(v, input);
        var version = v.RequiredVersion(input.Version);
        v.ThrowIfInvalid();

        db.ExpectVersion(eventType, version);
        fields.ApplyTo(eventType);
        await db.SaveChangesAsync(cancellationToken);
        return EventTypeDto.From(eventType);
    }

    /// <summary>Archived types stay on existing events but cannot be used for new ones.</summary>
    public async Task<EventTypeDto> ArchiveAsync(string key, CancellationToken cancellationToken)
    {
        var eventType = await db.EventTypes.FindOrThrowAsync(key, Resource, cancellationToken);
        if (eventType.ArchivedAt is null)
        {
            eventType.ArchivedAt = clock.GetCurrentInstant();
            await db.SaveChangesAsync(cancellationToken);
        }

        return EventTypeDto.From(eventType);
    }

    public async Task<EventTypeDto> RestoreAsync(string key, CancellationToken cancellationToken)
    {
        var eventType = await db.EventTypes.FindOrThrowAsync(key, Resource, cancellationToken);
        if (eventType.ArchivedAt is not null)
        {
            eventType.ArchivedAt = null;
            await db.SaveChangesAsync(cancellationToken);
        }

        return EventTypeDto.From(eventType);
    }

    private static EventTypeFields ValidateFields(InputValidator v, EventTypeInput input)
    {
        var displayName = v.RequiredText("displayName", input.DisplayName, InputValidator.ShortTextMaxLength);
        var category = v.Enum<EventCategory>("category", input.Category, required: true);
        var description = v.OptionalText("description", input.Description, 1000);
        var defaultAction = v.OptionalText("defaultActionTitle", input.DefaultActionTitle, InputValidator.NameMaxLength);
        return new EventTypeFields(displayName, category ?? EventCategory.Other, description, input.IsWindowByDefault ?? false, defaultAction);
    }

    private sealed record EventTypeFields(
        string DisplayName,
        EventCategory Category,
        string? Description,
        bool IsWindowByDefault,
        string? DefaultActionTitle)
    {
        public void ApplyTo(EventType target)
        {
            target.DisplayName = DisplayName;
            target.Category = Category;
            target.Description = Description;
            target.IsWindowByDefault = IsWindowByDefault;
            target.DefaultActionTitle = DefaultActionTitle;
        }
    }
}
