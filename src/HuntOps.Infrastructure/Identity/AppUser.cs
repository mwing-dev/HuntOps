using Microsoft.AspNetCore.Identity;

namespace HuntOps.Infrastructure.Identity;

/// <summary>A HuntOps login. V1 has exactly one, flagged <see cref="IsOwner"/> (enforced by a unique filtered index).</summary>
public sealed class AppUser : IdentityUser
{
    public bool IsOwner { get; set; }
}
