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
    public IReadOnlyList<string> RequiredPermissions => [Permissions.MembersInvite];
}

// Mints a new MemberId, materializes the aggregate, and pushes it into the
// repository. Demonstrates server-side derivation of the tenant_id: the new
// Member lands in the actor's tenant (taken from the JWT-projected
// actor.Attributes["tenant_id"]), NEVER in a tenant the caller could spoof
// by editing the request body.
public sealed class InviteMemberHandler : ICommandHandler<InviteMemberCommand, Result<MemberId>>
{
    private readonly IMemberRepository _repository;
    private readonly IActorProvider _actorProvider;
    private readonly TimeProvider _timeProvider;

    public InviteMemberHandler(IMemberRepository repository, IActorProvider actorProvider, TimeProvider timeProvider)
    {
        _repository = repository;
        _actorProvider = actorProvider;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<MemberId>> Handle(InviteMemberCommand command, CancellationToken cancellationToken)
    {
        // The actor and its tenant_id are guaranteed by the time the handler runs: IAuthorize ran the
        // static-permission gate first, and the actor provider's RequiredAttributes fails a missing
        // tenant_id closed at the JWT boundary. GetValueOrThrow makes that guarantee explicit.
        var actor = (await _actorProvider.GetCurrentActorAsync(cancellationToken))
            .GetValueOrThrow("Actor must be present; the IAuthorize pipeline guarantees it.");
        var tenantId = actor.GetRequiredAttribute<TenantId>("tenant_id")
            .GetValueOrThrow("tenant_id is a required actor attribute; the actor provider guarantees it.");

        // Mint a TENANT-SCOPED MemberId from "{tenantId}-{localPart}". The tenant prefix prevents
        // cross-tenant collisions by construction — without it, an attacker could invite
        // carol@anything.example and silently overwrite a 'carol' member in a different tenant. Production
        // would use a GUID-backed id (RequiredGuid<MemberId>) and a UNIQUE constraint on (TenantId, Email);
        // the template keeps the id human-readable for the demo trace.
        var localPart = command.Email.Split('@')[0];

        // Railway: build the tenant-scoped id (TryCreate already returns the right validation error if it
        // somehow fails), ensure it is not a same-tenant duplicate (Conflict does NOT leak cross-tenant
        // existence — the id is tenant-scoped), then stage the new member. Member.Invite raises the
        // MemberInvited domain event the outbox captures in the same transaction; the relay dispatches the
        // audit log + the integration-event translator after the commit, so neither fires for a member that
        // failed to persist.
        return await MemberId.TryCreate($"{tenantId.Value}-{localPart}")
            .EnsureAsync(
                async memberId => !await _repository.ExistsAsync(memberId, cancellationToken),
                memberId => Error.Conflict.For<Member>(memberId, "members.duplicate", "A member with this id already exists in this tenant."))
            .TapAsync(memberId => _repository.Add(
                Member.Invite(memberId, tenantId, command.Email, command.Role, _timeProvider)));
    }
}
