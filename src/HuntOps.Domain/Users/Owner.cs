namespace HuntOps.Domain.Users;

/// <summary>
/// V1 is optimized for a single owner (an ASP.NET Identity user flagged as owner).
/// </summary>
public static class Owner
{
    /// <summary>
    /// Placeholder user id written to user-scoped rows (action status history, API keys) before an owner account
    /// exists. Phase 2 wrote it for everything. The owner bootstrap (run by the migrate container) re-assigns all
    /// placeholder rows to the real owner's Identity id, so it is transient.
    /// </summary>
    public const string PlaceholderUserId = "owner";
}
