namespace TodoSample.Application.Todos;

using Mediator;
using TodoSample.Domain;
using Trellis.Authorization;

/// <summary>
/// Completes a todo item. Only the creator can complete their own todo.
/// <para>
/// Body-less state-transition POST. Does <strong>not</strong> require <c>If-Match</c>:
/// a supplied header is checked before mutation and a mismatch returns
/// <c>412 Precondition Failed</c>. Without a header, the state machine guard on
/// <see cref="TodoItem.Complete"/> still rejects invalid transitions with
/// <c>422 Unprocessable Content</c>.
/// </para>
/// </summary>
public sealed record CompleteTodoCommand : ICommand<Result<TodoItem>>, IAuthorize, IAuthorizeResource<TodoItem>, IIdentifyResource<TodoItem, TodoId>
{
    public TodoId TodoId { get; }
    public EntityTagValue[]? IfMatchETags { get; }

    private CompleteTodoCommand(TodoId todoId, EntityTagValue[]? ifMatchETags)
    {
        TodoId = todoId;
        IfMatchETags = ifMatchETags;
    }

    /// <summary>
    /// Creates an always-valid command. A null id fails closed as validation (422).
    /// </summary>
    public static Result<CompleteTodoCommand> TryCreate(TodoId? todoId, EntityTagValue[]? ifMatchETags = null) =>
        Result.EnsureNotNull(todoId, "id", "Todo id is required.")
            .Map(validId => new CompleteTodoCommand(validId, ifMatchETags));

    /// <inheritdoc />
    public IReadOnlyList<string> RequiredPermissions { get; } = [Permissions.TodosComplete];

    /// <inheritdoc />
    public IResult Authorize(Actor actor, TodoItem resource) =>
        Result.Ensure(actor.IsOwner(resource.CreatedByActorId),
            Error.Forbidden.For<TodoItem>(
                code: "todo.complete.creator-only",
                id: resource.Id,
                detail: "Only the creator can complete this todo."));

    /// <inheritdoc />
    public TodoId GetResourceId() => TodoId;
}

/// <summary>
/// Handler for CompleteTodoCommand.
/// </summary>
public sealed class CompleteTodoCommandHandler : ICommandHandler<CompleteTodoCommand, Result<TodoItem>>
{
    private readonly ITodoRepository _repository;
    private readonly TimeProvider _timeProvider;

    public CompleteTodoCommandHandler(ITodoRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<TodoItem>> Handle(CompleteTodoCommand command, CancellationToken cancellationToken) =>
        await _repository.FindByIdAsync(command.TodoId, cancellationToken)
            .ToResultAsync(Error.NotFound.For<TodoItem>(
                id: command.TodoId,
                detail: $"Todo {command.TodoId} not found."))
            .OptionalETagAsync(command.IfMatchETags)
            .CheckAsync(todo => todo.Complete(_timeProvider));
}
