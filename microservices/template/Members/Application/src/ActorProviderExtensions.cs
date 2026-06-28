using Trellis;
using Trellis.Authorization;

namespace ProjectTrackerTemplate.Members.Application;

// Reads the current actor's tenant_id for tenant-scoped handlers. By the time a handler runs the actor
// and its tenant_id are guaranteed present — IAuthorize ran the static-permission gate first, and the
// actor provider's RequiredAttributes closes a missing tenant_id at the JWT-validation boundary — so the
// unwrap is GetValueOrThrow (an assertion of that guarantee) rather than a re-checked Result path.
internal static class ActorProviderExtensions
{
    public static async ValueTask<TenantId> GetCurrentTenantIdAsync(
        this IActorProvider actorProvider, CancellationToken cancellationToken)
    {
        var actor = (await actorProvider.GetCurrentActorAsync(cancellationToken))
            .GetValueOrThrow("Actor must be present; the IAuthorize pipeline guarantees it.");
        return actor.GetRequiredAttribute<TenantId>("tenant_id")
            .GetValueOrThrow("tenant_id is a required actor attribute; the actor provider guarantees it.");
    }
}
