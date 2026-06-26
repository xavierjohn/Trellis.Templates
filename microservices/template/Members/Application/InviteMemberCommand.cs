using Mediator;
using ProjectTrackerTemplate.Members.Domain;
using ProjectTrackerTemplate.Members.Infrastructure;
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
        var actorMaybe = await _actorProvider.GetCurrentActorAsync(cancellationToken).ConfigureAwait(false);
        if (!actorMaybe.HasValue)
            return Result.Fail<MemberId>(new Error.AuthenticationRequired());

        if (!actorMaybe.Value.GetRequiredAttribute<TenantId>("tenant_id").TryGetValue(out var tenantId))
            return Result.Fail<MemberId>(new Error.AuthenticationRequired());

        // Mint a TENANT-SCOPED MemberId from "{tenantId}-{localPart}". The tenant
        // prefix prevents cross-tenant collisions by construction — without it, an
        // attacker could invite carol@anything.example and silently overwrite a
        // 'carol' member in a different tenant. Production would use a
        // GUID-backed id (RequiredGuid<MemberId>) and a UNIQUE constraint on
        // (TenantId, Email); the template keeps the id human-readable for the
        // demo trace.
        var localPart = command.Email.Split('@')[0];
        var idResult = MemberId.TryCreate($"{tenantId.Value}-{localPart}");
        if (!idResult.TryGetValue(out var memberId))
            return Result.Fail<MemberId>(Error.InvalidInput.ForRule("members.invalid_id", "Email local-part is not a valid MemberId."));

        // Same-tenant duplicate check. Returning Conflict here does NOT leak
        // cross-tenant existence because the id is tenant-scoped — only members
        // in the actor's own tenant can produce a collision, and the actor
        // already has visibility into their own tenant.
        var existing = await _repository.FindByIdAsync(memberId, cancellationToken).ConfigureAwait(false);
        if (existing.HasValue)
            return Result.Fail<MemberId>(new Error.Conflict(ResourceRef.For<Member>(memberId.Value), "members.duplicate"));

        var member = Member.Invite(memberId, tenantId, command.Email, command.Role, _timeProvider);

        // Stage the new member; the TransactionalCommandBehavior commits the unit of work when this
        // handler returns success. The "member invited" business event is NOT logged here — Member.Invite
        // raises a MemberInvited domain event, the outbox captures it in the SAME transaction as the
        // member row, and the relay dispatches it AFTER the commit: the audit log (MemberInvitedAuditLogger)
        // and the cross-service integration event (MemberInvitedTranslator) both hang off that event, so
        // neither can fire for a member that failed to persist.
        _repository.Add(member);

        return Result.Ok(memberId);
    }
}
