namespace ProjectTrackerTemplate.Gateway;

using Microsoft.Extensions.Options;
using Trellis;
using Trellis.Asp.Authorization;
using Trellis.Authorization;

internal sealed class GatewayClaimsActorProvider(
    IHttpContextAccessor accessor, IOptions<ClaimsActorOptions> options, ILogger<ClaimsActorProvider> logger)
    : ClaimsActorProvider(accessor, options, logger)
{
    public override async Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
    {
        var actor = await base.GetCurrentActorAsync(cancellationToken);
        return actor.Map(current => new Actor(current.Id, current.Permissions, current.ForbiddenPermissions,
            new Dictionary<string, string>(current.Attributes, StringComparer.Ordinal)
            {
                ["tenant_id"] = HttpContextAccessor.HttpContext?.User.FindFirst("tenant_id")?.Value
                    ?? throw new InvalidOperationException("The authenticated external token must contain tenant_id."),
            }));
    }
}