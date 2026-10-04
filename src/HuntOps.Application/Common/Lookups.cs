using HuntOps.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace HuntOps.Application.Common;

internal static class Lookups
{
    public static async Task<T> FindOrThrowAsync<T>(this DbSet<T> set, object id, string resource, CancellationToken cancellationToken)
        where T : class =>
        await set.FindAsync([id], cancellationToken) ?? throw new NotFoundException(resource, id);

    public static void EnsureActive(this IArchivable entity, string field, string description)
    {
        if (entity.ArchivedAt is not null)
        {
            throw new RequestValidationException(field, $"{description} is archived; restore it first or choose another.");
        }
    }
}
