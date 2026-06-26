using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectTrackerTemplate.Members.Domain;
using Trellis.EntityFrameworkCore;

namespace ProjectTrackerTemplate.Members.Infrastructure;

// EF Core mapping for the Member aggregate. The value-object columns (Id, TenantId) get their
// string conversions from ApplyTrellisConventionsFor; this only fixes column lengths (so the key
// and the indexed TenantId are not nvarchar(max)) and adds the tenant lookup index.
internal sealed class MemberConfiguration : IEntityTypeConfiguration<Member>
{
    public void Configure(EntityTypeBuilder<Member> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasMaxLength(128);
        builder.Property(m => m.TenantId).IsRequired().HasMaxLength(64);
        builder.Property(m => m.Email).IsRequired().HasMaxLength(256);
        builder.Property(m => m.Role).IsRequired().HasMaxLength(64);

        // ListByTenantAsync filters on TenantId.
        builder.HasTrellisIndex(m => new { m.TenantId });
    }
}
