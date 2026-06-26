using Microsoft.EntityFrameworkCore;
using ProjectTrackerTemplate.Members.Domain;
using Trellis.EntityFrameworkCore;

namespace ProjectTrackerTemplate.Members.Infrastructure;

// EF Core context for the Members service. ApplyTrellisConventionsFor maps the value objects
// (MemberId, TenantId) to columns, the aggregate ETag to a concurrency token, and Maybe<T> to
// nullable columns. AddTrellisInterceptors (wired in Program.cs) stamps the ETag + Created/
// LastModified timestamps on save.
public class MembersDbContext : DbContext
{
    public DbSet<Member> Members => Set<Member>();

    public MembersDbContext(DbContextOptions<MembersDbContext> options)
        : base(options)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.ApplyTrellisConventionsFor<MembersDbContext>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MembersDbContext).Assembly);
}
