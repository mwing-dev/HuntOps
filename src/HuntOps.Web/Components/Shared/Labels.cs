using HuntOps.Domain.Actions;
using HuntOps.Domain.Events;
using MudBlazor;

namespace HuntOps.Web.Components.Shared;

/// <summary>Human labels and colors for domain values.</summary>
public static class Labels
{
    public static string Status(EffectiveActionStatus status) => status switch
    {
        EffectiveActionStatus.Upcoming => "Upcoming",
        EffectiveActionStatus.Open => "Open now",
        EffectiveActionStatus.Missed => "Missed",
        EffectiveActionStatus.Completed => "Completed",
        EffectiveActionStatus.NotApplicable => "Not applicable",
        EffectiveActionStatus.Cancelled => "Cancelled",
        _ => status.ToString(),
    };

    public static Color StatusColor(EffectiveActionStatus status) => status switch
    {
        EffectiveActionStatus.Open => Color.Warning,
        EffectiveActionStatus.Missed => Color.Error,
        EffectiveActionStatus.Completed => Color.Success,
        EffectiveActionStatus.Upcoming => Color.Info,
        _ => Color.Default,
    };

    public static string Resolution(ActionResolution resolution) => resolution switch
    {
        ActionResolution.Completed => "Completed",
        ActionResolution.NotApplicable => "Not applicable",
        ActionResolution.Cancelled => "Cancelled",
        ActionResolution.Reopened => "Reopened",
        _ => resolution.ToString(),
    };

    public static string Category(EventCategory category) => category switch
    {
        EventCategory.Application => "Application",
        EventCategory.Purchase => "Sale / purchase",
        EventCategory.Results => "Results",
        EventCategory.Reporting => "Reporting",
        EventCategory.Season => "Season",
        _ => "Other",
    };

    /// <summary>CSS class used by the calendar (app.css).</summary>
    public static string CategoryCss(EventCategory category) => "cat-" + category.ToString().ToLowerInvariant();

    public static Color CategoryColor(EventCategory category) => category switch
    {
        EventCategory.Application => Color.Primary,
        EventCategory.Purchase => Color.Secondary,
        EventCategory.Results => Color.Info,
        EventCategory.Reporting => Color.Warning,
        EventCategory.Season => Color.Tertiary,
        _ => Color.Default,
    };

    public static string Phase(EventPhase phase) => phase switch
    {
        EventPhase.Upcoming => "Upcoming",
        EventPhase.Open => "Open now",
        _ => "Closed",
    };

    /// <summary>Preset outcomes offered when resolving an action (free text is always allowed).</summary>
    public static readonly IReadOnlyList<string> OutcomePresets =
    [
        "Applied",
        "Preference point purchased",
        "Drawn",
        "Not drawn",
        "License purchased",
        "Report submitted",
    ];
}
