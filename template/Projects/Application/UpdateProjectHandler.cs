using Mediator;
using ProjectTrackerTemplate.Projects.Domain;
using Trellis;
using Trellis.Authorization;

namespace ProjectTrackerTemplate.Projects.Application;

// The mutation path. Reads the SAME Project instance ResourceAuthorizationBehavior
// loaded for Authorize, mutates it in place, returns Result.Ok(Unit.Value).
//
// In-memory store quirk: the dictionary holds the SAME reference, so mutating
// the instance from the accessor automatically updates the "stored" project
// without a save call. A real EF service would call SaveChangesAsync (or rely
// on TransactionalCommandBehavior from Trellis.EntityFrameworkCore).
public sealed class UpdateProjectHandler : ICommandHandler<UpdateProjectCommand, Result<Trellis.Unit>>
{
    private readonly IAuthorizedResource<UpdateProjectCommand, Project> _authorized;

    public UpdateProjectHandler(IAuthorizedResource<UpdateProjectCommand, Project> authorized) => _authorized = authorized;

    public ValueTask<Result<Trellis.Unit>> Handle(UpdateProjectCommand command, CancellationToken cancellationToken)
    {
        _authorized.GetRequiredResource().Update(command.Title, command.Description);
        return new(Result.Ok());
    }
}
