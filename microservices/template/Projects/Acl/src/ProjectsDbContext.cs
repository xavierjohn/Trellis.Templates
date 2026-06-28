using Microsoft.EntityFrameworkCore;
using ProjectTrackerTemplate.Projects.Domain;
using ProjectTrackerTemplate.Projects.ReadModel;
using Trellis.EntityFrameworkCore;

namespace ProjectTrackerTemplate.Projects.Acl;

// EF Core context for the Projects service: the Project aggregate it owns, the inbox dedup table, and the
// team read model it builds from other services' integration events. ApplyTrellisConventionsFor maps the
// value-object columns (ProjectId/TenantId/Title/Description on the aggregate, TenantId on the read model)
// and the aggregate ETag to a concurrency token; AddTrellisInbox maps the (ConsumerId, MessageId) dedup
// table the inbox dispatcher writes.
public class ProjectsDbContext : DbContext
{
    public DbSet<Project> Projects => Set<Project>();

    public DbSet<KnownMember> KnownMembers => Set<KnownMember>();

    public ProjectsDbContext(DbContextOptions<ProjectsDbContext> options)
        : base(options)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.ApplyTrellisConventionsFor<ProjectsDbContext>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProjectsDbContext).Assembly);

        // Map the inbox dedup table (TrellisInboxMessages, composite (ConsumerId, MessageId) key). The
        // inbox dispatcher writes a row here in the SAME SaveChanges as the handler's read-model update,
        // so an event's effect and its dedup record commit atomically.
        modelBuilder.AddTrellisInbox();
    }
}
