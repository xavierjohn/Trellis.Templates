using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectTrackerTemplate.Projects.ReadModel;

namespace ProjectTrackerTemplate.Projects.Infrastructure;

// EF mapping for the KnownMember read model. The TenantId value-object column conversion comes from
// ApplyTrellisConventionsFor; this fixes the composite key, the column widths, and the role/timestamp.
internal sealed class KnownMemberConfiguration : IEntityTypeConfiguration<KnownMember>
{
    public void Configure(EntityTypeBuilder<KnownMember> builder)
    {
        builder.ToTable("KnownMembers");

        // Composite key: a member is unique within a tenant. Keeping one row per (TenantId, MemberId)
        // makes a re-inserted duplicate a primary-key collision — which the handler's existence check
        // avoids, keeping it safe to re-run on a Service Bus redelivery.
        builder.HasKey(km => new { km.TenantId, km.MemberId });
        builder.Property(km => km.TenantId).HasMaxLength(64);
        builder.Property(km => km.MemberId).HasMaxLength(128);
        builder.Property(km => km.Role).IsRequired().HasMaxLength(64);
    }
}
