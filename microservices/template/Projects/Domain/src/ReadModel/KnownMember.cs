namespace ProjectTrackerTemplate.Projects.ReadModel;

// Read model (projection) — NOT an aggregate. Projects builds it purely from Members' MemberInvited
// integration events, so it can answer "who is on this tenant's team?" from its OWN store with no
// synchronous call back to Members. This is eventual-consistency replication across bounded contexts:
// the data is owned by the Members context and replicated here as a local, queryable copy.
//
// TenantId is the shared-kernel value object (Projects' own tenant identity). MemberId is an OPAQUE
// string — a foreign identity minted by the Members context that Projects never interprets, only stores.
public sealed class KnownMember
{
    // EF Core materialization constructor.
    private KnownMember()
    {
    }

    public KnownMember(TenantId tenantId, string memberId, string role, DateTimeOffset invitedAt)
    {
        TenantId = tenantId;
        MemberId = memberId;
        Role = role;
        InvitedAt = invitedAt;
    }

    public TenantId TenantId { get; private set; } = null!;

    public string MemberId { get; private set; } = null!;

    public string Role { get; private set; } = null!;

    public DateTimeOffset InvitedAt { get; private set; }
}
