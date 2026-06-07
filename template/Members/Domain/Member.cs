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
public sealed class Member
{
    public Member(MemberId id, string tenantId, string email, string role)
    {
        Id = id;
        TenantId = tenantId;
        Email = email;
        Role = role;
    }

    public MemberId Id { get; }

    // The tenant this member belongs to. The resource-auth pipeline +
    // HideExistence<Member>() collapses cross-tenant access into 404.
    public string TenantId { get; }

    // PII. Production would project this through a value object that
    // applies redaction in toString/log output and uses a value-object-based
    // equality comparer that hashes the input rather than comparing raw strings
    // when used as a dictionary key. The template keeps the body trivial.
    public string Email { get; private set; }

    public string Role { get; private set; }

    public void UpdateRole(string role) => Role = role;
}
