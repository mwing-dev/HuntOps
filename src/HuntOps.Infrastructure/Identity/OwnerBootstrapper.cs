using System.ComponentModel.DataAnnotations;
using HuntOps.Application.Users;
using HuntOps.Domain.Users;
using HuntOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HuntOps.Infrastructure.Identity;

public enum OwnerBootstrapOutcome
{
    /// <summary>No users existed; the owner was created from HUNTOPS_ADMIN_EMAIL / HUNTOPS_ADMIN_PASSWORD.</summary>
    Created,

    /// <summary>An owner already exists; bootstrap variables (if any) were ignored.</summary>
    AlreadyExists,

    /// <summary>No users exist and the bootstrap variables are not set. Nobody can log in until they are.</summary>
    NotConfigured,

    /// <summary>The bootstrap variables are set but invalid (bad email, weak password). Nothing was created.</summary>
    InvalidConfiguration,
}

public sealed record OwnerBootstrapResult(
    OwnerBootstrapOutcome Outcome,
    string Message,
    int ReassignedStatusChanges = 0,
    int ReassignedApiKeys = 0);

/// <summary>
/// Creates the single owner account and adopts placeholder-owned data. Run by the migrate container after
/// migrations on every start, so it is idempotent:
/// <list type="number">
/// <item>Zero users + HUNTOPS_ADMIN_EMAIL/PASSWORD set: create the owner (password policy enforced).
/// Never creates default credentials; never logs the password.</item>
/// <item>An owner exists: the bootstrap variables are ignored (they never overwrite an existing password).</item>
/// <item>Re-assign rows still owned by the placeholder user id "owner" (Phase 2 data, or keys made with the CLI
/// before the owner existed) to the owner's Identity id, in one transaction. History is preserved as-is apart
/// from the owning user id; the database trigger permits exactly this one change.</item>
/// <item>Ensure owner settings exist (defaults; HUNTOPS_OWNER_TIMEZONE).</item>
/// </list>
/// </summary>
public sealed partial class OwnerBootstrapper(
    HuntOpsDbContext db,
    UserManager<AppUser> users,
    IConfiguration configuration,
    IOptions<OwnerDefaultsOptions> ownerDefaults,
    ILogger<OwnerBootstrapper> logger)
{
    public const string EmailVariable = "HUNTOPS_ADMIN_EMAIL";
    public const string PasswordVariable = "HUNTOPS_ADMIN_PASSWORD";

    public async Task<OwnerBootstrapResult> RunAsync(CancellationToken cancellationToken)
    {
        var ownerId = await db.Users.Where(u => u.IsOwner).Select(u => u.Id).FirstOrDefaultAsync(cancellationToken);
        var outcome = OwnerBootstrapOutcome.AlreadyExists;

        if (ownerId is null)
        {
            if (await db.Users.AnyAsync(cancellationToken))
            {
                // Users exist but none is flagged as owner: refuse to guess.
                const string ambiguous = "User accounts exist but none is marked as the owner; refusing to bootstrap another account.";
                LogInvalid(logger, ambiguous);
                return new OwnerBootstrapResult(OwnerBootstrapOutcome.InvalidConfiguration, ambiguous);
            }

            var created = await CreateOwnerAsync(cancellationToken);
            if (created.Result is not null)
            {
                return created.Result;
            }

            ownerId = created.OwnerId!;
            outcome = OwnerBootstrapOutcome.Created;
        }
        else if (!string.IsNullOrEmpty(configuration[PasswordVariable]))
        {
            LogBootstrapIgnored(logger, PasswordVariable);
        }

        var (statusChanges, apiKeys) = await AdoptPlaceholderDataAsync(ownerId, cancellationToken);
        await EnsureSettingsAsync(ownerId, cancellationToken);

        var message = outcome == OwnerBootstrapOutcome.Created ? "Owner account created." : "Owner account already exists.";
        return new OwnerBootstrapResult(outcome, message, statusChanges, apiKeys);
    }

    private async Task<(string? OwnerId, OwnerBootstrapResult? Result)> CreateOwnerAsync(CancellationToken cancellationToken)
    {
        var email = configuration[EmailVariable]?.Trim();
        var password = configuration[PasswordVariable];

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            var message = $"No owner account exists and {EmailVariable} / {PasswordVariable} are not set. " +
                          "Nobody can sign in to the dashboard until they are set and the stack is restarted.";
            LogNotConfigured(logger, message);
            return (null, new OwnerBootstrapResult(OwnerBootstrapOutcome.NotConfigured, message));
        }

        if (!new EmailAddressAttribute().IsValid(email))
        {
            var message = $"{EmailVariable} is not a valid email address.";
            LogInvalid(logger, message);
            return (null, new OwnerBootstrapResult(OwnerBootstrapOutcome.InvalidConfiguration, message));
        }

        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, IsOwner = true, LockoutEnabled = true };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            // Identity error descriptions describe the rule ("Passwords must have at least one digit"), never the value.
            var message = $"Owner account was not created: {string.Join(" ", result.Errors.Select(e => e.Description))}";
            LogInvalid(logger, message);
            return (null, new OwnerBootstrapResult(OwnerBootstrapOutcome.InvalidConfiguration, message));
        }

        cancellationToken.ThrowIfCancellationRequested();
        LogCreated(logger, email);
        return (user.Id, null);
    }

    /// <summary>Re-assigns placeholder-owned API keys and action history to the owner (one transaction).</summary>
    private async Task<(int StatusChanges, int ApiKeys)> AdoptPlaceholderDataAsync(string ownerId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var apiKeys = await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE api_keys SET user_id = {ownerId} WHERE user_id = {Owner.PlaceholderUserId}", cancellationToken);
        var statusChanges = await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE action_status_changes SET user_id = {ownerId} WHERE user_id = {Owner.PlaceholderUserId}", cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (apiKeys + statusChanges > 0)
        {
            LogAdopted(logger, statusChanges, apiKeys);
        }

        return (statusChanges, apiKeys);
    }

    private async Task EnsureSettingsAsync(string ownerId, CancellationToken cancellationToken)
    {
        if (await db.OwnerSettings.AnyAsync(s => s.UserId == ownerId, cancellationToken))
        {
            return;
        }

        var timeZone = NodaTime.DateTimeZoneProviders.Tzdb.GetZoneOrNull(ownerDefaults.Value.TimeZoneId) is null
            ? OwnerSettings.DefaultTimeZoneId
            : ownerDefaults.Value.TimeZoneId;
        db.OwnerSettings.Add(new OwnerSettings { UserId = ownerId, TimeZoneId = timeZone });
        await db.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Created owner account {Email}")]
    private static partial void LogCreated(ILogger logger, string email);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Message}")]
    private static partial void LogNotConfigured(ILogger logger, string message);

    [LoggerMessage(Level = LogLevel.Error, Message = "Owner bootstrap failed: {Message}")]
    private static partial void LogInvalid(ILogger logger, string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "An owner account already exists; {Variable} is ignored (it never changes an existing password). You can remove it from .env.")]
    private static partial void LogBootstrapIgnored(ILogger logger, string variable);

    [LoggerMessage(Level = LogLevel.Information, Message = "Assigned placeholder-owned data to the owner: {StatusChanges} action status change(s), {ApiKeys} API key(s)")]
    private static partial void LogAdopted(ILogger logger, int statusChanges, int apiKeys);
}
