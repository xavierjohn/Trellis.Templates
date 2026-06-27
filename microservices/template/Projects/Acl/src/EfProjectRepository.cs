using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectTrackerTemplate.Projects.Application;
using ProjectTrackerTemplate.Projects.Domain;
using Trellis;
using Trellis.EntityFrameworkCore;

namespace ProjectTrackerTemplate.Projects.Acl;

// EF Core implementation of the Project repository. Add, ExistsAsync, etc. are inherited from
// RepositoryBase<Project, ProjectId> (Add only STAGES — the unit of work commits on command-handler
// success). FindByIdAsync is overridden ONLY to keep the projects.resource_loads instrumentation that
// proves the load-once invariant; the tenant-scoped list query lives here too.
internal sealed partial class EfProjectRepository : RepositoryBase<Project, ProjectId>, IProjectRepository
{
    private readonly ILogger<EfProjectRepository> _logger;

    public EfProjectRepository(ProjectsDbContext context, ILogger<EfProjectRepository> logger)
        : base(context) => _logger = logger;

    // FindByIdAsync emits the projects.resource_loads counter + a structured log line on EVERY call —
    // the falsifiable proof of the "load once" invariant. If a handler bypasses the resource-auth
    // accessor and re-loads here, the counter shows N=2 per single request.
    public override async Task<Maybe<Project>> FindByIdAsync(ProjectId id, CancellationToken cancellationToken = default)
    {
        ProjectsMetrics.ResourceLoads.Add(1, new KeyValuePair<string, object?>("project.id", id.Value));
        LogProjectResourceLoaded(_logger, id.Value);

        return await base.FindByIdAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<Project>> ListByTenantAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        await DbSet
            .Where(p => p.TenantId == tenantId)
            .OrderBy(p => p.Id)
            .ToListAsync(cancellationToken);

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        EventName = "ProjectResourceLoaded",
        Message = "Loaded Project {ProjectId} from the ACL repository. This is the signal that proves load-once.")]
    private static partial void LogProjectResourceLoaded(ILogger logger, string projectId);
}
