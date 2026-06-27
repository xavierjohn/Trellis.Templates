using ProjectTrackerTemplate.Members.Application;
using ProjectTrackerTemplate.Members.Domain;
using Trellis;
using Trellis.Authorization;

namespace ProjectTrackerTemplate.Members.Acl;

// Loader for the resource-authorization pipeline. Bridges Maybe<Member> ->
// Result<Member>, translating "not found" into Error.NotFound.
//
// The pipeline composes this with HideExistence<Member>() so cross-tenant
// auth failures (Error.Forbidden) are projected to Error.NotFound at the
// response-mapping stage — the caller cannot distinguish "I do not have
// permission to see this Member" from "no such Member".
public sealed class MemberResourceLoader : SharedResourceLoaderById<Member, MemberId>
{
    private readonly IMemberRepository _repository;

    public MemberResourceLoader(IMemberRepository repository) => _repository = repository;

    public override Task<Result<Member>> GetByIdAsync(MemberId id, CancellationToken cancellationToken) =>
        _repository.FindByIdAsync(id, cancellationToken)
            .ToResultAsync(new Error.NotFound(ResourceRef.For<Member>(id.Value)));
}
