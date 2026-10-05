using HuntOps.Application.Abstractions;
using HuntOps.Application.Common;
using HuntOps.Domain.Common;
using MudBlazor;

namespace HuntOps.Web.Services;

/// <summary>Errors for one form: field messages from the application layer plus a general message.</summary>
public sealed class FormErrors
{
    private Dictionary<string, string[]> _fields = new(StringComparer.Ordinal);

    public string? General { get; set; }

    public bool IsConcurrencyConflict { get; set; }

    public bool Any => General is not null || _fields.Count > 0;

    public bool Has(string field) => _fields.ContainsKey(field);

    public string? For(string field) => _fields.TryGetValue(field, out var messages) ? string.Join(" ", messages) : null;

    /// <summary>Field errors the form does not display next to an input (shown in the summary instead).</summary>
    public IEnumerable<string> Unmapped(IEnumerable<string> shownFields)
    {
        var shown = shownFields.ToHashSet(StringComparer.Ordinal);
        return _fields.Where(f => !shown.Contains(f.Key)).SelectMany(f => f.Value.Select(m => $"{Humanize(f.Key)}: {m}"));
    }

    public void SetFields(IReadOnlyDictionary<string, string[]> fields) =>
        _fields = fields.ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal);

    public void Clear()
    {
        _fields.Clear();
        General = null;
        IsConcurrencyConflict = false;
    }

    private static string Humanize(string field) =>
        string.Concat(field.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : i == 0 ? char.ToUpperInvariant(c).ToString() : c.ToString()));
}

/// <summary>
/// Runs dashboard operations and turns application/database errors into messages. The application layer stays
/// authoritative for validation; the UI only displays what it reports.
/// </summary>
public sealed partial class UiErrors(IDatabaseErrorClassifier databaseErrors, ILogger<UiErrors> logger)
{
    public const string ConcurrencyMessage = "This item changed since you opened it. Reload and review the latest version.";

    /// <summary>Runs <paramref name="operation"/>; on failure fills <paramref name="errors"/> and returns false.</summary>
    public async Task<bool> TryAsync(Func<Task> operation, FormErrors errors)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(errors);
        errors.Clear();
        try
        {
            await operation();
            return true;
        }
        catch (Exception ex) when (Describe(ex) is { } problem)
        {
            if (problem.Fields is not null)
            {
                errors.SetFields(problem.Fields);
            }

            errors.General = problem.Message;
            errors.IsConcurrencyConflict = problem.IsConcurrency;
            return false;
        }
    }

    /// <summary>Runs <paramref name="operation"/>; on failure shows a snackbar and returns false.</summary>
    public async Task<bool> TryAsync(Func<Task> operation, ISnackbar snackbar)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(snackbar);
        try
        {
            await operation();
            return true;
        }
        catch (Exception ex) when (Describe(ex) is { } problem)
        {
            var details = problem.Fields is null ? "" : " " + string.Join(" ", problem.Fields.SelectMany(f => f.Value));
            snackbar.Add(problem.Message + details, Severity.Error);
            return false;
        }
    }

    private Problem? Describe(Exception exception)
    {
        switch (exception)
        {
            case RequestValidationException validation:
                return new Problem("Please fix the highlighted fields.", validation.Errors);
            case DomainValidationException domain:
                return new Problem("Please fix the highlighted fields.", domain.Errors);
            case NotFoundException notFound:
                return new Problem(notFound.Message);
            case ConflictException conflict:
                return new Problem(conflict.Message);
        }

        if (databaseErrors.Classify(exception) is { } db)
        {
            return db.Kind switch
            {
                DatabaseErrorKind.ConcurrencyConflict => new Problem(ConcurrencyMessage, IsConcurrency: true),
                DatabaseErrorKind.UniqueViolation => new Problem("A record with the same unique values already exists."),
                DatabaseErrorKind.CheckViolation => new Problem("The data violates a database rule."),
                _ => new Problem("The change would break a reference between records."),
            };
        }

        LogUnexpected(logger, exception);
        return new Problem("Something went wrong. The details were logged on the server.");
    }

    private sealed record Problem(string Message, IReadOnlyDictionary<string, string[]>? Fields = null, bool IsConcurrency = false);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected error in a dashboard operation")]
    private static partial void LogUnexpected(ILogger logger, Exception exception);
}
