using Mediator;
using ProjectTrackerTemplate.Members.Domain;
using Trellis;
using Trellis.Authorization;

namespace ProjectTrackerTemplate.Members.Application;

// Invite a new member to the actor's tenant. The new member always lands in
// the actor's tenant (server-side decision; the wire format does NOT accept
// a tenant_id parameter so a malicious caller cannot drop a member into a
// different tenant). The command embeds only the public-facing fields.
public sealed record InviteMemberCommand(string Email, string Role)
    : ICommand<Result<MemberId>>, IAuthorize
{
    public IReadOnlyList<string> RequiredPermissions => ["members:invite"];
}
