using Microsoft.AspNetCore.Identity;

namespace HuntOps.Infrastructure.Identity;

/// <summary>Password, lockout and user rules for the owner account. Used by bootstrap and login alike.</summary>
public static class IdentityPolicy
{
    public const int MinimumPasswordLength = 12;
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public static void Apply(IdentityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Password.RequiredLength = MinimumPasswordLength;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequiredUniqueChars = 5;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = MaxFailedAttempts;
        options.Lockout.DefaultLockoutTimeSpan = LockoutDuration;

        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedAccount = false;
        options.SignIn.RequireConfirmedEmail = false;
    }
}
