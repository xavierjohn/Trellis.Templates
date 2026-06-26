using Trellis;

namespace ProjectTrackerTemplate.Members.Domain;

// Member aggregate — an HR-sensitive identity inside a single tenant.
//
// What makes this "HR-sensitive": cross-tenant access is BOTH a 403 AND a 404.
// The Projects service responds 403 on cross-tenant access (telling the caller
// "this resource exists but you can't see it"). The Members service uses
// HideExistence<Member>() so cross-tenant access returns 404 ("this member
// does not exist as far as you're concerned"). The difference matters when the
// resource identifier itself is sensitive — e.g., an attacker enumerating
// member IDs to discover the existence of employees across tenants.
// Now a Trellis Aggregate<MemberId>: it inherits an ETag concurrency token and Created/LastModified
// timestamps (stamped by the EF interceptors), and can raise domain events the outbox captures.
public sealed class Member : Aggregate<MemberId>
{
    // EF Core materialization constructor. The materializer sets the key + required scalars.
    private Member()
        : base(default!)
    {
    }

    public Member(MemberId id, TenantId tenantId, string email, string role)
        : base(id)
    {
        TenantId = tenantId;
        Email = email;
        Role = role;
    }

    // The tenant this member belongs to. The resource-auth pipeline +
    // HideExistence<Member>() collapses cross-tenant access into 404.
    public TenantId TenantId { get; private set; } = null!;

    // PII. Production would project this through a value object that applies redaction in
    // ToString/log output. The template keeps the body trivial.
    public string Email { get; private set; } = null!;

    public string Role { get; private set; } = null!;

    public void UpdateRole(string role) => Role = role;
}
