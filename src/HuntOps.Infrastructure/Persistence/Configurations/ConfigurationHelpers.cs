using HuntOps.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HuntOps.Infrastructure.Persistence.Configurations;

internal static class ConfigurationHelpers
{
    public const string ActiveFilter = "archived_at IS NULL";

    /// <summary>Guid key, audit timestamps and xmin-based optimistic concurrency.</summary>
    public static void ConfigureEntity<T>(this EntityTypeBuilder<T> builder)
        where T : Entity
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Version).IsRowVersion();
    }

    /// <summary>Stores an enum as its name and adds a CHECK constraint listing the allowed names.</summary>
    public static PropertyBuilder<TEnum> EnumAsText<TEntity, TEnum>(
        this EntityTypeBuilder<TEntity> builder,
        System.Linq.Expressions.Expression<Func<TEntity, TEnum>> property,
        string table,
        string column)
        where TEntity : class
        where TEnum : struct, Enum
    {
        var allowed = string.Join(", ", Enum.GetNames<TEnum>().Select(n => $"'{n}'"));
        builder.ToTable(t => t.HasCheckConstraint($"ck_{table}_{column}", $"{column} IN ({allowed})"));
        return builder.Property(property).HasConversion<string>().HasMaxLength(32);
    }
}
