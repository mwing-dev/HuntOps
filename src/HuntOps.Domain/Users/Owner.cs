namespace HuntOps.Domain.Users;

/// <summary>
/// V1 is optimized for a single owner. User-scoped rows (action status history, API keys) carry this id until
/// ASP.NET Identity arrives in Phase 3, whose migration re-points them to the owner's Identity user id.
/// </summary>
public static class Owner
{
    public const string UserId = "owner";
}
