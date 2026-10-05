using HuntOps.Domain.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HuntOps.Infrastructure.Persistence.Configurations;

internal sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("api_keys", t =>
        {
            // Only a 32-byte SHA-256 hash is ever stored; there is no column that could hold the plaintext key.
            t.HasCheckConstraint("ck_api_keys_hash_sha256", "octet_length(hash) = 32");
            t.HasCheckConstraint("ck_api_keys_prefix_format", "prefix ~ '^hops_[a-z0-9]{8}$'");
            t.HasCheckConstraint("ck_api_keys_scopes", "scopes BETWEEN 1 AND 3");
        });
        builder.HasKey(k => k.Id);
        builder.Property(k => k.Id).ValueGeneratedNever();
        builder.Property(k => k.Name).HasMaxLength(100);
        builder.Property(k => k.Prefix).HasMaxLength(13);
        builder.Property(k => k.Hash).HasMaxLength(32);
        builder.Property(k => k.Scopes).HasConversion<int>();
        builder.Property(k => k.UserId).HasMaxLength(100);
        builder.Property(k => k.CreatedBy).HasMaxLength(200);
        builder.HasIndex(k => k.Prefix).IsUnique();
    }
}
