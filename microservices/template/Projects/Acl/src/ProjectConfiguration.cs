using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectTrackerTemplate.Projects.Domain;
using Trellis.EntityFrameworkCore;

namespace ProjectTrackerTemplate.Projects.Acl;

// EF Core mapping for the Project aggregate. The value-object columns (Id, TenantId, Title, Description)
// get their string conversions from ApplyTrellisConventionsFor; this fixes the column widths (so the key
// and the indexed TenantId are not nvarchar(max)) and adds the tenant lookup index for ListByTenantAsync.
internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasMaxLength(128);
        builder.Property(p => p.OwnerId).IsRequired().HasMaxLength(128);
        builder.Property(p => p.TenantId).IsRequired().HasMaxLength(64);
        builder.Property(p => p.Title).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Description).IsRequired().HasMaxLength(1000);

        // ListByTenantAsync filters on TenantId.
        builder.HasTrellisIndex(p => new { p.TenantId });
    }
}
