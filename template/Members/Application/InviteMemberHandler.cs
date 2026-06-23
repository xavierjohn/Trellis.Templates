using Mediator;
using ProjectTrackerTemplate.Members.Domain;
using ProjectTrackerTemplate.Members.Infrastructure;
using Trellis;
using Trellis.Authorization;

namespace ProjectTrackerTemplate.Members.Application;

// Mints a new MemberId, materializes the aggregate, and pushes it into the
// repository. Demonstrates server-side derivation of the tenant_id: the new
// Member lands in the actor's tenant (taken from the JWT-projected
// actor.Attributes["tenant_id"]), NEVER in a tenant the caller could spoof
// by editing the request body.
public sealed class InviteMemberHandler : ICommandHandler<InviteMemberCommand, Result<MemberId>>
{
    private readonly IMemberRepository _repository;
    private readonly IActorProvider _actorProvider;

    public InviteMemberHandler(IMemberRepository repository, IActorProvider actorProvider)
    {
        _repository = repository;
        _actorProvider = actorProvider;
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

        var member = new Member(memberId, tenantId, command.Email, command.Role);
        await _repository.AddAsync(member, cancellationToken).ConfigureAwait(false);

        return Result.Ok(memberId);
    }
}
