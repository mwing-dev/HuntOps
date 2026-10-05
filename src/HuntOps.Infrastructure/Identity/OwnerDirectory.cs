using HuntOps.Application.Users;
using HuntOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HuntOps.Infrastructure.Identity;

internal sealed class OwnerDirectory(HuntOpsDbContext db) : IOwnerDirectory
{
    public Task<string?> GetOwnerUserIdAsync(CancellationToken cancellationToken) =>
        db.Users.Where(u => u.IsOwner).Select(u => u.Id).FirstOrDefaultAsync(cancellationToken);
}
