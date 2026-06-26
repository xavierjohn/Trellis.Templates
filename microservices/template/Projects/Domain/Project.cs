namespace ProjectTrackerTemplate.Projects.Domain;

// Project aggregate — a unit of work owned by a principal (actor) inside a single tenant.
//
// In-memory mutable POCO for the template starter. A real production service would
// derive from Trellis.Authorization.Aggregate<ProjectId> and use value objects for
// the body fields. The template keeps the body intentionally minimal so the AUTH
// pipeline is the only moving piece you have to read on a first scan.
//
// Notably mutable: `Update(...)` mutates in place. That mutation is what proves the
// v4 accessor pattern works — the handler reads the SAME instance the auth pipeline
// loaded, mutates it, and the change persists in the in-memory store.
public sealed class Project
{
    public Project(ProjectId id, string ownerId, TenantId tenantId, string title, string description)
    {
        Id = id;
        OwnerId = ownerId;
        TenantId = tenantId;
        Title = title;
        Description = description;
    }

    public ProjectId Id { get; }

    // The actor (principal) id of the owner — e.g. "alice", which is distinct from that person's
    // tenant-scoped MemberId ("acme-alice"). Resource-based authorization compares it against
    // Actor.Id.Value in UpdateProjectCommand.Authorize.
    public string OwnerId { get; }

    // The tenant this project belongs to. Cross-tenant access is rejected
    // at the resource-auth boundary in BOTH Get and Update handlers — projects
    // tell the caller their request is forbidden (403), unlike Members which
    // hide existence (404).
    public TenantId TenantId { get; }

    public string Title { get; private set; }

    public string Description { get; private set; }

    public void Update(string title, string description)
    {
        // Intentionally trivial — template starter focuses on the auth pipeline.
        // A production aggregate would Result-check inputs via Trellis.Core's
        // Result.Ensure + Trellis.FluentValidation, and Update would become
        // `Result<Unit> Update(...)` so the handler can Bind on it.
        Title = title;
        Description = description;
    }
}
