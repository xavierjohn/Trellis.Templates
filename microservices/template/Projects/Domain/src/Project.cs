using Trellis;

namespace ProjectTrackerTemplate.Projects.Domain;

// Project aggregate — a unit of work owned by a principal (actor) inside a single tenant.
//
// A Trellis Aggregate<ProjectId>: it inherits an ETag concurrency token and Created/LastModified
// timestamps (stamped by the EF interceptors on save). The ETag is what lets the update endpoint
// enforce an RFC 9110 If-Match precondition and reject a stale write with 412. The body fields are
// value objects (ProjectTitle, ProjectDescription) so an empty or over-long value is rejected at the
// boundary (422) rather than persisted.
//
// Update(...) mutates in place; the resource-auth pipeline loads the aggregate once and the handler
// reads + mutates that SAME tracked instance, which the unit of work commits.
public sealed class Project : Aggregate<ProjectId>
{
    // EF Core materialization constructor. The materializer sets the key + required scalars.
    private Project()
        : base(default!)
    {
    }

    public Project(ProjectId id, string ownerId, TenantId tenantId, ProjectTitle title, ProjectDescription description)
        : base(id)
    {
        OwnerId = ownerId;
        TenantId = tenantId;
        Title = title;
        Description = description;
    }

    // The actor (principal) id of the owner — e.g. "alice", which is distinct from that person's
    // tenant-scoped MemberId ("acme-alice"). Resource-based authorization compares it against
    // Actor.Id.Value in UpdateProjectCommand.Authorize. It is an external identity (a JWT subject),
    // so it stays a plain string.
    public string OwnerId { get; private set; } = null!;

    // The tenant this project belongs to. Cross-tenant access is rejected at the resource-auth
    // boundary in BOTH Get and Update handlers — projects tell the caller their request is forbidden
    // (403), unlike Members which hide existence (404).
    public TenantId TenantId { get; private set; } = null!;

    public ProjectTitle Title { get; private set; } = null!;

    public ProjectDescription Description { get; private set; } = null!;

    public void Update(ProjectTitle title, ProjectDescription description)
    {
        Title = title;
        Description = description;
    }
}
