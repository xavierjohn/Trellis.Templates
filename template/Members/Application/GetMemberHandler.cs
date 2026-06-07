using Mediator;
using ProjectTrackerTemplate.Members.Domain;
using Trellis;
using Trellis.Authorization;

namespace ProjectTrackerTemplate.Members.Application;

// Reads the SAME Member instance ResourceAuthorizationBehavior loaded for
// Authorize — no second repository round-trip.
public sealed class GetMemberHandler : IQueryHandler<GetMemberQuery, Result<Member>>
{
    private readonly IAuthorizedResource<GetMemberQuery, Member> _authorized;

    public GetMemberHandler(IAuthorizedResource<GetMemberQuery, Member> authorized) => _authorized = authorized;

    public ValueTask<Result<Member>> Handle(GetMemberQuery query, CancellationToken cancellationToken) =>
        new(Result.Ok(_authorized.GetRequiredResource()));
}
