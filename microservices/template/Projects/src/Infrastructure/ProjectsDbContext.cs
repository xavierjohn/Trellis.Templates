using Microsoft.EntityFrameworkCore;
using ProjectTrackerTemplate.Projects.ReadModel;
using Trellis.EntityFrameworkCore;

namespace ProjectTrackerTemplate.Projects.Infrastructure;

// EF Core context for the Projects service's CONSUMER state: the inbox dedup table and the read models
// it builds from other services' integration events. The Project aggregate itself stays in the in-memory
// repository (the template's auth walkthrough) — this context exists purely for the cross-service
// eventing plane, so the two storage mechanisms read side by side as a deliberate contrast.
//
// ApplyTrellisConventionsFor maps the value-object columns (TenantId on the read model); AddTrellisInbox
// maps the (ConsumerId, MessageId) dedup table the inbox dispatcher writes.
public class ProjectsDbContext : DbContext
{
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
