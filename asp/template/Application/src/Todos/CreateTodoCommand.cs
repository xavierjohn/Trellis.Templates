namespace TodoSample.Application.Todos;

using FluentValidation;
using Mediator;
using TodoSample.Domain;
using Trellis.Authorization;

/// <summary>
/// Creates a new todo item. Always-valid at construction via <see cref="TryCreate"/>.
/// </summary>
public sealed record CreateTodoCommand : ICommand<Result<TodoItem>>, IAuthorize
{
    /// <summary>Title of the todo.</summary>
    public Title Title { get; }

    /// <summary>Due date for the todo.</summary>
    public DueDate DueDate { get; }

    /// <summary>Optional categorization tag.</summary>
    public Maybe<Tag> Tag { get; }

    /// <inheritdoc />
    public IReadOnlyList<string> RequiredPermissions { get; } = [Permissions.TodosCreate];

    private CreateTodoCommand(Title title, DueDate dueDate, Maybe<Tag> tag)
    {
        Title = title;
        DueDate = dueDate;
        Tag = tag;
    }

    /// <summary>
    /// Creates an always-valid command. A missing required field — <paramref name="title"/> or
    /// <paramref name="dueDate"/> bind to <c>null</c> when the JSON omits them — fails closed as
    /// validation (422) here, rather than surfacing later as a <see cref="NullReferenceException"/> (500).
    /// The tag is optional (<see cref="Maybe{T}"/>).
    /// </summary>
    public static Result<CreateTodoCommand> TryCreate(Title? title, DueDate? dueDate, Maybe<Tag> tag) =>
        Result.Ensure(title is not null, Error.InvalidInput.ForField("title", "required", "Title is required."))
            .Combine(Result.Ensure(dueDate is not null, Error.InvalidInput.ForField("dueDate", "required", "Due date is required.")))
            .Map(_ => new CreateTodoCommand(title!, dueDate!, tag));
}

/// <summary>
/// FluentValidation showcase. Construction-time invariants (required fields, cross-field rules) live in
/// <see cref="CreateTodoCommand.TryCreate"/>; this validator is the seam for command rules that need
/// injected services or async I/O — which a pure, synchronous <c>TryCreate</c> cannot express.
/// </summary>
public sealed class CreateTodoCommandValidator : AbstractValidator<CreateTodoCommand>
{
    public CreateTodoCommandValidator()
    {
        // Example (DI/async rule that TryCreate cannot do):
        //   RuleFor(command => command.Title).MustAsync((title, ct) => _policy.IsAllowedAsync(title, ct));
    }
}

/// <summary>
/// Handler for CreateTodoCommand.
/// </summary>
public sealed class CreateTodoCommandHandler : ICommandHandler<CreateTodoCommand, Result<TodoItem>>
{
    private readonly ITodoRepository _repository;
    private readonly IActorProvider _actorProvider;
    private readonly TimeProvider _timeProvider;

    public CreateTodoCommandHandler(ITodoRepository repository, IActorProvider actorProvider, TimeProvider timeProvider)
    {
        _repository = repository;
        _actorProvider = actorProvider;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<TodoItem>> Handle(CreateTodoCommand command, CancellationToken cancellationToken)
    {
        var actor = (await _actorProvider.GetCurrentActorAsync(cancellationToken))
            .GetValueOrThrow("Actor must be present; IAuthorize pipeline guarantees this.");
        var todo = new TodoItem(command.Title, command.DueDate, command.Tag, actor.Id, _timeProvider);
        return Result.Ok(todo)
            .Check(t => t.Start())
            .Tap(_repository.Add);
    }
}
