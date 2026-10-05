namespace TodoSample.Application.Todos;

using Mediator;
using TodoSample.Domain;
using Trellis.Authorization;

/// <summary>
/// Updates a todo item's title, due date, and tag.
/// Always valid at construction — DueDate must be in the future.
/// </summary>
public sealed record UpdateTodoCommand : ICommand<Result<TodoItem>>, IAuthorize
{
    public TodoId TodoId { get; }
    public Title Title { get; }
    public DueDate DueDate { get; }
    public Maybe<Tag> Tag { get; }

    /// <summary>
    /// The ETag from the client's <c>If-Match</c> header.
    /// <para>
    /// Required (RFC 6585). When the array is <c>null</c>, the handler returns
    /// <c>new Error.TransportFault(new HttpError.PreconditionRequired(...))</c> which surfaces as
    /// <c>428 Precondition Required</c>. When provided, the handler validates it against the
    /// aggregate's current ETag before mutation, returning <c>412 Precondition Failed</c> if
    /// stale (RFC 9110).
    /// </para>
    /// </summary>
    public EntityTagValue[]? IfMatchETags { get; }

    /// <inheritdoc />
    public IReadOnlyList<string> RequiredPermissions { get; } = [Permissions.TodosUpdate];

    private UpdateTodoCommand(TodoId todoId, Title title, DueDate dueDate, Maybe<Tag> tag, EntityTagValue[]? ifMatchETags)
    {
        TodoId = todoId;
        Title = title;
        DueDate = dueDate;
        Tag = tag;
        IfMatchETags = ifMatchETags;
    }

    /// <summary>
    /// Creates an always-valid command. A missing (null) required field fails closed as validation
    /// (422); when present, <paramref name="dueDate"/> must be in the future.
    /// </summary>
    /// <param name="todoId">The todo to update.</param>
    /// <param name="title">New title.</param>
    /// <param name="dueDate">New due date (must be in the future).</param>
    /// <param name="tag">New optional tag.</param>
    /// <param name="ifMatchETags">Optional ETags from the <c>If-Match</c> header for conditional update.</param>
    /// <param name="timeProvider">Optional time provider for testability. Defaults to <see cref="TimeProvider.System"/>.</param>
    public static Result<UpdateTodoCommand> TryCreate(TodoId? todoId, Title? title, DueDate? dueDate, Maybe<Tag> tag, EntityTagValue[]? ifMatchETags = null, TimeProvider? timeProvider = null) =>
        todoId.ToResult(Error.InvalidInput.ForField(
            code: "required",
            field: "id",
            detail: "Todo id is required."))
            .Combine(title.ToResult(Error.InvalidInput.ForField(
                code: "required",
                field: "title",
                detail: "Title is required.")))
            .Combine(dueDate.ToResult(Error.InvalidInput.ForField(
                code: "required",
                field: "dueDate",
                detail: "Due date is required.")))
            .Ensure(values => values.Item3 > (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime,
                Error.InvalidInput.ForField(
                    code: "out_of_range",
                    field: "dueDate",
                    detail: "Due date must be in the future."))
            .Map((validId, validTitle, validDueDate) => new UpdateTodoCommand(validId, validTitle, validDueDate, tag, ifMatchETags));
}

/// <summary>
/// Handler for UpdateTodoCommand.
/// </summary>
public sealed class UpdateTodoCommandHandler : ICommandHandler<UpdateTodoCommand, Result<TodoItem>>
{
    private readonly ITodoRepository _repository;

    public UpdateTodoCommandHandler(ITodoRepository repository) => _repository = repository;

    public async ValueTask<Result<TodoItem>> Handle(UpdateTodoCommand command, CancellationToken cancellationToken) =>
        await _repository.FindByIdAsync(command.TodoId, cancellationToken)
            .ToResultAsync(Error.NotFound.For<TodoItem>(
                id: command.TodoId,
                detail: $"Todo {command.TodoId} not found."))
            .RequireETagAsync(command.IfMatchETags)
            .BindAsync(todo => todo.Update(command.Title, command.DueDate, command.Tag));
}
