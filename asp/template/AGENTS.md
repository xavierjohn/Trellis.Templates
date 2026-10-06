# Agent Instructions — Building with Trellis
<!-- agentdocs:start -->
**Read `.agentdocs/README.md` now.** The path is relative to this instruction file (repository-root path: `.agentdocs/README.md`).
<!-- agentdocs:end -->


This template builds ASP.NET Core services on the Trellis framework for .NET 10.

## Generated profile

Read `.trellis-template.json` before changing composition or endpoints. APIs are unversioned unless
`apiVersioning` is `"true"`; versioned examples below apply only to that profile. Unversioned controllers
and models use `Api/src/Controllers`, `Api/src/Models`, and undated namespaces. Do not add versioning
packages, dated namespaces, or version parameters to their Location/pagination links.
Keep the selected database, external identity provider, exporters, and deployment mode consistent.
Development actors are not a production authentication mechanism.
Todo due dates are UTC instants. API requests must supply `Z` or an explicit offset, normalized by
`DueDateJsonConverter`; timezone-free dates are rejected with 422. Construct `DueDate` only from UTC
`DateTime` values, and preserve UTC kind when rehydrating this column with any database provider.

## 🔴 Before Writing Code — Read AgentDocs

**STOP. Do not write or generate any code until you have read the reference material for your task.** These files document the exact method signatures, overloads, conventions, and EF Core mapping rules. Guessing based on type names will produce code that compiles but fails at runtime (e.g., adding explicit EF `Property()` configuration on types that Trellis conventions already handle).

**Start at [`.agentdocs/README.md`](.agentdocs/README.md).** It identifies the required router for each project and the task-specific references to open on demand.

**Do not try to read the whole set.** It is roughly 300K tokens; skimming it burns the budget you need for the task itself. Follow the project groups in `.agentdocs/README.md`, then use the required Trellis router to open only the one to three area references or recipe bodies the task needs. Never write code from a recipe's title alone; open the body.

**Read the references yourself — do not delegate them to a sub-agent.** A sub-agent hands back a summary, so the exact signatures never reach your context and you end up writing code against a paraphrase. That is how invented APIs and wrong overloads get produced, and it is the specific failure these references exist to prevent. Sub-agents are fine for work whose output is a *verdict* — running builds and tests, searching for a file — but if the answer determines the code you are about to write, read it yourself.

**Reference docs are authoritative.** If anything in this file conflicts with one of the `trellis-*.md` reference files, the reference file wins — AgentDocs installs those version-aligned files from the restored, approved packages. This file is curated guidance that can drift. Please file any contradiction as feedback.

**Known erratum — Trellis 3.0.0-alpha.542, cookbook Recipe 23:** its claim that guarded transitions can omit precondition checking is incorrect. Follow this guide's supplied `If-Match` rule below: the header may be optional, but `OptionalETag` must enforce it when present. This narrow HTTP-policy correction overrides that recipe's contrary wording, not the authoritative API signatures. Keep managed package references unchanged until a corrected framework package is published and synced.

| When working on... | Read first |
|---|---|
| **Anything — start here.** Task routing, recipes, preflight, inherited surface | `.agentdocs/packages/trellis.core/trellis/trellis-start-here.md` |
| `Result<T>`, `Maybe<T>`, `Error`, `Bind`, `Map`, `Tap`, `Ensure`, `Combine`, `ParallelAsync` | `.agentdocs/packages/trellis.core/trellis/trellis-api-core.md` |
| Aggregates, entities, value objects, specifications, ETag checks | `.agentdocs/packages/trellis.core/trellis/trellis-api-core.md` |
| `RequiredString<T>`, `RequiredGuid<T>`, `RequiredEnum<T>`, built-in primitives | `.agentdocs/packages/trellis.core/trellis/trellis-api-primitives.md` |
| MVC/Minimal API result mappers, `ETagHelper`, scalar binding, validation middleware | `.agentdocs/packages/trellis.core/trellis/trellis-api-asp.md` |
| EF Core conventions, interceptors, `HasTrellisIndex`, `FirstOrDefaultMaybeAsync` | `.agentdocs/packages/trellis.core/trellis/trellis-api-efcore.md` |
| Actor-based authorization, `IAuthorize`, resource authorization | `.agentdocs/packages/trellis.core/trellis/trellis-api-authorization.md` |
| FluentValidation bridge: `AddTrellisFluentValidation` DI registration + pipeline adapter | `.agentdocs/packages/trellis.core/trellis/trellis-api-mediator-fluentvalidation.md` |
| FluentValidation bridge: low-level `IResult` converters + JSON-pointer normalization | `.agentdocs/packages/trellis.core/trellis/trellis-api-fluentvalidation.md` |
| `HttpClient` result extensions | `.agentdocs/packages/trellis.core/trellis/trellis-api-http.md` |
| Mediator pipeline behaviors | `.agentdocs/packages/trellis.core/trellis/trellis-api-mediator.md` |
| `LazyStateMachine<TState, TTrigger>` and `FireResult()` | `.agentdocs/packages/trellis.core/trellis/trellis-api-statemachine.md` |
| Testing helpers, `FakeRepository`, `TestActorProvider`, assertions, `Unwrap()` | `.agentdocs/packages/trellis.core/trellis/trellis-api-testing-reference.md` |
| ASP.NET Core integration tests, `WebApplicationFactory` helpers, `.http` replay | `.agentdocs/packages/trellis.core/trellis/trellis-api-testing-aspnetcore.md` |
| Fixing an analyzer warning — ready-to-apply WRONG/FIX shapes | `.agentdocs/packages/trellis.core/trellis/trellis-api-anti-patterns.md` |
| Analyzer diagnostics `TRLS001`–`TRLS0xx` and generator diagnostics | `.agentdocs/packages/trellis.core/trellis/trellis-api-analyzers.md` |
| Cross-package patterns, recipes, and task lookup table | `.agentdocs/packages/trellis.core/trellis/trellis-api-cookbook.md` |
| Scalar vs composite value-object classification | `.agentdocs/packages/trellis.core/trellis/trellis-value-object-taxonomy.md` |

AgentDocs installs the complete `Trellis.Core` reference set plus guidance from the separately approved ResourceNaming and SLI packages. `.agentdocs/README.md` is the generated index; route through it rather than opening files speculatively.

**A reference file being present does not mean the project you are editing can use that package.** Before writing code against one, confirm the target project has a `<PackageReference>` for it in its own `.csproj` — `Directory.Packages.props` only supplies the version for centrally managed packages and lists some that no project references. If it is absent, say what adopting the package would buy rather than emitting code that cannot compile.

## Critical Rules

### Study the template reference implementation first

- **Rule:** 🔴 MUST read the Todo sample before replacing it.
- **Rationale:** The shipped sample demonstrates the exact Trellis patterns this template expects.
- **Correct:** Use the reference implementation table below and inspect the listed files before generating your own service.
- **Incorrect:** Recreate the solution structure and patterns from scratch without checking the working sample.
- **Reference:** See `Domain/src/`, `Application/src/`, `Acl/src/`, `Api/src/`.

### Treat errors and optional values as explicit types

- **Rule:** 🔴 MUST use `Result<T>` for expected failures and `Maybe<T>` for optional values. Never throw for business logic. Never use `try/catch` in Domain or Application layers for expected outcomes.
- **Rationale:** Trellis relies on Railway Oriented Programming; exceptions for expected paths break the pipeline and reduce testability.
- **Exceptions are still for the _exceptional_.** The rule is "never throw for an **expected** outcome" (validation, not-found, conflict, forbidden, optional absence — model these as `Result<T>` / `Maybe<T>`), **not** "never throw at all". `throw` remains correct for unrecoverable faults that signal a bug or broken environment: API misuse, failed startup/configuration checks, and infrastructure errors. For internal "shouldn't happen" faults you may also return the value `Error.Unexpected(reasonCode, faultId?)` instead of throwing. Analyzer **TRLS010** enforces the no-throw rule inside Result chains (`Bind`/`Map`/`Tap`/`Ensure`).
- **Correct:**
```csharp
using Trellis;

public static Result<Order> TryCreate(OrderName name) =>
    string.IsNullOrWhiteSpace(name.Value)
        ? Result.Fail<Order>(Error.InvalidInput.ForField(
            code: "required",
            field: "name",
            detail: "Name is required."))
        : Result.Ok(new Order(name));

public partial class Customer : Aggregate<CustomerId>
{
    public partial Maybe<PhoneNumber> PhoneNumber { get; private set; }
}
```
- **Incorrect:**
```csharp
using Trellis;

public static Order Create(string name)
{
    if (string.IsNullOrWhiteSpace(name))
        throw new InvalidOperationException("Name is required.");

    return new Order(name);
}

public sealed class Customer
{
    public PhoneNumber? PhoneNumber { get; private set; }
}
```
- **Reference:** See `.agentdocs/packages/trellis.core/trellis/trellis-api-core.md`, `.agentdocs/packages/trellis.core/trellis/trellis-api-efcore.md`.

### Eliminate primitive obsession on domain surfaces

- **Rule:** 🔴 MUST expose value objects on aggregates, entities, commands, and public domain methods. Do not expose raw `Guid`, `string`, `int`, or `decimal` for domain concepts.
- **Rationale:** Trellis models validity at the type level; primitive-based domain APIs reintroduce invalid states.
- **Correct:**
```csharp
using Trellis;

public sealed record UpdateTodoCommand(TodoId TodoId, Title Title, DueDate DueDate);

public partial class Order : Aggregate<OrderId>
{
    public OrderStatus Status { get; private set; } = null!;
    public CustomerId CustomerId { get; private set; } = null!;
}
```
- **Incorrect:**
```csharp
public sealed record UpdateTodoCommand(Guid TodoId, string Title, DateTime DueDate);

public sealed class Order
{
    public string Status { get; private set; } = string.Empty;
    public Guid CustomerId { get; private set; }
}
```
- **Reference:** See `.agentdocs/packages/trellis.core/trellis/trellis-api-primitives.md`, `.agentdocs/packages/trellis.core/trellis/trellis-value-object-taxonomy.md`.

### Use `RequiredEnum<T>` for all domain enum-like concepts

- **Rule:** 🔴 MUST model domain enums as `RequiredEnum<T>` partial classes, not C# `enum`.
- **Rationale:** `RequiredEnum<T>` gives validation, JSON conversion, EF Core conversion, LINQ support, and attachable behavior.
- **Correct:**
```csharp
using Trellis;

public partial class OrderStatus : RequiredEnum<OrderStatus>
{
    public static readonly OrderStatus Draft = new();
    public static readonly OrderStatus Confirmed = new();
    public static readonly OrderStatus Shipped = new();
    public static readonly OrderStatus Cancelled = new();
}

public partial class PaymentMethod : RequiredEnum<PaymentMethod>
{
    [EnumValue("credit-card")]
    public static readonly PaymentMethod CreditCard = new();

    [EnumValue("bank-transfer")]
    public static readonly PaymentMethod BankTransfer = new();

    public static readonly PaymentMethod Cash = new();
}

public partial class FulfillmentStatus : RequiredEnum<FulfillmentStatus>
{
    public static readonly FulfillmentStatus Draft = new(canModify: true, isTerminal: false);
    public static readonly FulfillmentStatus Confirmed = new(canModify: false, isTerminal: false);
    public static readonly FulfillmentStatus Cancelled = new(canModify: false, isTerminal: true);

    public bool CanModify { get; }
    public bool IsTerminal { get; }

    private FulfillmentStatus(bool canModify, bool isTerminal)
    {
        CanModify = canModify;
        IsTerminal = isTerminal;
    }
}
```
- **Incorrect:**
```csharp
public enum OrderStatus
{
    Draft,
    Confirmed,
    Shipped,
    Cancelled
}
```
- **Reference:** See `.agentdocs/packages/trellis.core/trellis/trellis-api-primitives.md §RequiredEnum<TSelf>` and `.agentdocs/packages/trellis.core/trellis/trellis-api-efcore.md §ModelConfigurationBuilderExtensions`.
### Make commands always-valid and time-testable

- **Rule:** 🔴 MUST make commands receive value objects, always expose a **private constructor plus a static `TryCreate(...)` returning `Result<T>`** (which fails closed — 422 — on a missing/`null` required field and on any cross-field invariant, so the command is un-representable in an invalid state), and use `TimeProvider` instead of `DateTime.UtcNow` or `DateTimeOffset.UtcNow`. Controllers construct commands via `TryCreate(...).BindAsync(command => _sender.Send(command, ct))`, never `new XyzCommand(...)`. (Queries stay plain records — their inputs are route/query-bound scalars already validated at the binder seam.)
- **Rationale:** Command validity belongs at construction time, and time-dependent rules must remain testable.
- **Correct:**
```csharp
using Mediator;
using Trellis;
using Trellis.Authorization;

public sealed record UpdateTodoCommand : ICommand<Result<TodoItem>>, IAuthorize
{
    public TodoId TodoId { get; }
    public Title Title { get; }
    public DueDate DueDate { get; }

    private UpdateTodoCommand(TodoId todoId, Title title, DueDate dueDate)
    {
        TodoId = todoId;
        Title = title;
        DueDate = dueDate;
    }

    public static Result<UpdateTodoCommand> TryCreate(
        TodoId todoId,
        Title title,
        DueDate dueDate,
        TimeProvider? timeProvider = null) =>
        Result.Ensure(
                dueDate > (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime,
                Error.InvalidInput.ForField(
                    code: "out_of_range",
                    field: "dueDate",
                    detail: "Due date must be in the future."))
            .Map(_ => new UpdateTodoCommand(todoId, title, dueDate));
}

public Result<Order> Approve(TimeProvider timeProvider) =>
    _machine.FireResult(Triggers.Approve)
        .Tap(order => DomainEvents.Add(new OrderApprovedEvent(Id, OccurredAt: timeProvider.GetUtcNow().UtcDateTime)))
        .Map(_ => this);
```
- **Incorrect:**
```csharp
using Mediator;

public sealed record UpdateTodoCommand(Guid TodoId, string Title, DateTime DueDate) : ICommand<Result<TodoItem>>;

public Result<Order> Approve() =>
    _machine.FireResult(Triggers.Approve)
        .Tap(_ => DomainEvents.Add(new OrderApprovedEvent(Id, OccurredAt: DateTime.UtcNow)))
        .Map(_ => this);
```
- **Reference:** See `.agentdocs/packages/trellis.core/trellis/trellis-api-core.md`, `.agentdocs/packages/trellis.core/trellis/trellis-api-cookbook.md`.

### Keep each command/query and its handler in one file

- **Rule:** 🔴 MUST colocate a command/query and its handler in the **same file**, named after the message (e.g. `Application/src/Todos/UpdateTodoCommand.cs` contains both `UpdateTodoCommand` and `UpdateTodoCommandHandler`). Do not split the handler into a separate `*Handler.cs`. (Domain-event subscribers such as `TodoCreatedLoggingHandler` are not command handlers and keep their own file.)
- **Rationale:** A command and its handler are one feature slice — reading or changing the behaviour means reading both. One file per feature keeps the slice cohesive, makes the message-to-handler mapping obvious, and avoids a parallel folder of handlers that drifts out of step with its messages.
- **Correct:**
```csharp
// Application/src/Todos/UpdateTodoCommand.cs — record and handler together
public sealed record UpdateTodoCommand : ICommand<Result<TodoItem>>, IAuthorize
{
    // ... value-object properties, private ctor + TryCreate, RequiredPermissions ...
}

public sealed class UpdateTodoCommandHandler : ICommandHandler<UpdateTodoCommand, Result<TodoItem>>
{
    private readonly ITodoRepository _repository;

    public UpdateTodoCommandHandler(ITodoRepository repository) => _repository = repository;

    // The unit-of-work commits on handler success; the handler only loads + mutates.
    public async ValueTask<Result<TodoItem>> Handle(UpdateTodoCommand command, CancellationToken cancellationToken) =>
        await _repository.FindByIdAsync(command.TodoId, cancellationToken)
            .ToResultAsync(Error.NotFound.For<TodoItem>(
                id: command.TodoId,
                detail: $"Todo {command.TodoId} not found."))
            .RequireETagAsync(command.IfMatchETags)
            .BindAsync(todo => todo.Update(command.Title, command.DueDate, command.Tag));
}
```
- **Incorrect:** `Application/src/Todos/UpdateTodoCommand.cs` holding only the record, with `UpdateTodoCommandHandler.cs` in a separate `Handlers/` folder.
- **Reference:** See `Application/src/Todos/UpdateTodoCommand.cs`, `Application/src/Todos/CompleteTodoCommand.cs`, `Application/src/Todos/DeleteTodoCommand.cs`, `Application/src/Todos/GetTodoByIdQuery.cs`.

### Declare permissions as constants in the Domain layer

- **Rule:** 🔴 MUST declare permission scopes as `public const string` members of a `public static class Permissions` in the **Domain** project, and reference them from commands/queries via `IAuthorize.RequiredPermissions` (e.g. `[Permissions.TodosUpdate]`). Never hard-code permission strings at the call site, and never put the constants in the Application or Api layer.
- **Rationale:** Permissions are a domain vocabulary (what the service allows), so they belong with the domain. Centralizing them as typed constants prevents string drift between the command that requires a permission and the policy/seed that grants it, and keeps the authorization surface auditable in one place.
- **Correct:**
```csharp
// Domain/src/Permissions.cs
namespace TodoSample.Domain;

public static class Permissions
{
    public const string TodosRead = "todos:read";
    public const string TodosUpdate = "todos:update";
}

// Application/src/Todos/UpdateTodoCommand.cs
public sealed record UpdateTodoCommand : ICommand<Result<TodoItem>>, IAuthorize
{
    public IReadOnlyList<string> RequiredPermissions { get; } = [Permissions.TodosUpdate];
    // ...
}
```
- **Incorrect:**
```csharp
// ❌ Magic string at the call site, no shared constant.
public IReadOnlyList<string> RequiredPermissions { get; } = ["todos:update"];

// ❌ Permission constants living in Application or Api (wrong layer).
```
- **Reference:** See `Domain/src/Permissions.cs` and its consumers in `Application/src/Todos/`.

### Build layer-by-layer and compile between layers

- **Rule:** 🔴 MUST implement Domain → Application → Acl → Api → Tests, running `dotnet build` between layers and `dotnet test` after tests are added.
- **Rationale:** Trellis uses source generators for `partial Maybe<T>` properties and Mediator code; later layers depend on generated output from earlier builds.
- **Correct:**
```text
1. Domain/src      -> dotnet build
2. Application/src -> dotnet build
3. Acl/src         -> dotnet build
4. Api/src         -> dotnet build
5. Tests           -> dotnet test
```
- **Incorrect:** Create all files across all projects first, then attempt a single build after generated code is already required by downstream layers.
- **Reference:** See the `## Implementation Order and Build Checkpoints` section below.

### Run `dotnet test` without legacy VSTest arguments

- **Rule:** 🔴 MUST NOT pass legacy VSTest arguments such as `--nologo`, `--logger`, `-l`, or `--results-directory` to `dotnet test`. Test projects in this template use xUnit v3 + Microsoft.Testing.Platform (MTP), which forwards unknown arguments to the test host and exits with code 5 plus `Zero tests ran` when it sees a flag it doesn't recognize — easily misread as a test failure.
- **Rationale:** MTP does not share a CLI surface with the legacy VSTest runner. `Unknown option '--nologo'` followed by `Zero tests ran` and `Exit code: 5` is the diagnostic signature of this mistake.
- **Correct:**
```powershell
dotnet test                                     # all defaults
dotnet test --no-build                          # skip rebuild
dotnet test -- --filter-not-trait "Category=Integration"
dotnet test -- --coverage --report-trx
```
- **Incorrect:**
```powershell
dotnet test --nologo                            # Unknown option '--nologo' -> exit code 5
dotnet test --logger trx                        # Unknown option '--logger' -> exit code 5
dotnet test -l "console;verbosity=minimal"      # rejected by MTP runner
```
- **Reference:** `runtests.cmd` (at the repo root) and `.github/workflows/build.yml` show the CI invocation. For the full MTP option list, run any compiled test exe directly: `./bin/Debug/net10.0/*.Tests.exe --help`.

### Return `Maybe<T>` from repository lookups

- **Rule:** 🔴 MUST return `Maybe<T>` from repository lookups and convert to `Result<T>` in handlers with `.ToResult(Error.NotFound.For<T>(id: id))`.
- **Rationale:** Absence is data, not failure; handlers own the domain meaning of “not found”.
- **Correct:**
```csharp
using Trellis;

public interface ITodoRepository
{
    Task<Maybe<TodoItem>> FindByIdAsync(TodoId id, CancellationToken cancellationToken);
}

public async ValueTask<Result<TodoItem>> Handle(GetTodoByIdQuery query, CancellationToken cancellationToken)
{
    var maybe = await _repository.FindByIdAsync(query.TodoId, cancellationToken);
    return maybe.ToResult(Error.NotFound.For<TodoItem>(
        id: query.TodoId,
        detail: "Todo not found."));
}
```
- **Incorrect:**
```csharp
using Trellis;

public interface ITodoRepository
{
    Task<Result<TodoItem>> FindByIdAsync(TodoId id, CancellationToken cancellationToken);
}
```
- **Reference:** See `.agentdocs/packages/trellis.core/trellis/trellis-api-core.md`, `.agentdocs/packages/trellis.core/trellis/trellis-api-efcore.md §QueryableExtensions`.

### Keep handlers on the ROP track

- **Rule:** 🔴 MUST compose handler flows with `Bind`, `BindAsync`, `CheckAsync`, `Map`, and related result combinators. Do not unwrap and branch imperatively unless branching materially improves readability.
- **Rationale:** ROP chains preserve failure propagation and keep success paths explicit.
- **Correct:**
```csharp
using Trellis;

public async ValueTask<Result<Order>> Handle(SubmitOrderCommand command, CancellationToken cancellationToken) =>
    await _orderRepository.GetByIdAsync(command.OrderId, cancellationToken)
        .BindAsync(order => order.Submit())
        .BindAsync(order => _orderRepository.SaveAsync(order, cancellationToken).MapAsync(_ => order));
```
- **Incorrect:**
```csharp
public async ValueTask<Result<Order>> Handle(SubmitOrderCommand command, CancellationToken cancellationToken)
{
    var result = await _orderRepository.GetByIdAsync(command.OrderId, cancellationToken);
    if (result.IsFailure)
        return result.Error;

    var order = result.Value;
    var submitResult = order.Submit();
    if (submitResult.IsFailure)
        return submitResult.Error;

    await _orderRepository.SaveAsync(order, cancellationToken);
    return order;
}
```
- **Reference:** See `.agentdocs/packages/trellis.core/trellis/trellis-api-core.md`, `.agentdocs/packages/trellis.core/trellis/trellis-api-cookbook.md`, `.agentdocs/packages/trellis.core/trellis/trellis-api-mediator.md`.

### Use `LazyStateMachine<TState, TTrigger>` in aggregates

- **Rule:** 🔴 MUST use `LazyStateMachine<TState, TTrigger>` instead of constructing `StateMachine<TState, TTrigger>` eagerly inside persisted aggregates.
- **Rationale:** EF Core materializes aggregates before state properties are populated; eager state-machine initialization can throw `NullReferenceException`.
- **Correct:**
```csharp
using Stateless;
using Trellis.StateMachine;

private readonly LazyStateMachine<OrderStatus, string> _machine;

private Order() : base(default!)
{
    _machine = new LazyStateMachine<OrderStatus, string>(
        () => Status,
        state => Status = state,
        ConfigureStateMachine);
}

private static void ConfigureStateMachine(StateMachine<OrderStatus, string> machine)
{
    // Configure transitions here.
}

public Result<OrderStatus> Submit() => _machine.FireResult("Submit");
```
- **Incorrect:**
```csharp
using Stateless;

private readonly StateMachine<OrderStatus, string> _machine;

private Order() : base(default!)
{
    _machine = new StateMachine<OrderStatus, string>(() => Status, state => Status = state);
}
```
- **Reference:** See `.agentdocs/packages/trellis.core/trellis/trellis-api-statemachine.md §LazyStateMachine<TState, TTrigger>` and `.agentdocs/packages/trellis.core/trellis/trellis-api-statemachine.md §StateMachineExtensions`.
### Follow Trellis EF Core conventions exactly

- **Rule:** 🔴 MUST use `ApplyTrellisConventionsFor<TContext>()`, `AddTrellisInterceptors`, `SaveChangesResultUnitAsync`, `partial Maybe<T>` properties, `HasTrellisIndex`, and EF materialization boilerplate exactly as Trellis expects.
- **Rationale:** Trellis persistence relies on conventions and generators; overriding them with manual EF patterns silently breaks mapping, timestamps, or generated backing fields.
- **Correct:**
```csharp
using Microsoft.EntityFrameworkCore;
using Trellis.EntityFrameworkCore;

public class Customer : Aggregate<CustomerId>
{
    public FirstName FirstName { get; private set; } = null!;
    public LastName LastName { get; private set; } = null!;
    public EmailAddress Email { get; private set; } = null!;
    public ShippingAddress ShippingAddress { get; private set; } = null!;
    public partial Maybe<PhoneNumber> Phone { get; set; }

    private Customer() : base(default!) { }
}

protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
    configurationBuilder.ApplyTrellisConventionsFor<AppDbContext>();

protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
    optionsBuilder.AddTrellisInterceptors();

builder.HasTrellisIndex(x => new { x.Name, x.SubmittedAt });

return await _context.SaveChangesResultUnitAsync(cancellationToken);
```
- **Incorrect:**
```csharp
using Microsoft.EntityFrameworkCore;

protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
{
}

builder.Property(x => x.Status).HasConversion<string>();
builder.OwnsOne(x => x.Money);
builder.HasIndex(x => new { x.Status, x.SubmittedAt });

return await _context.SaveChangesAsync(cancellationToken);
```
- **Reference:** See `.agentdocs/packages/trellis.core/trellis/trellis-api-efcore.md §DbContextOptionsBuilderExtensions`, `.agentdocs/packages/trellis.core/trellis/trellis-api-efcore.md §ModelConfigurationBuilderExtensions`, `.agentdocs/packages/trellis.core/trellis/trellis-api-efcore.md §DbContextExtensions`, `.agentdocs/packages/trellis.core/trellis/trellis-api-efcore.md §MaybeEntityTypeBuilderExtensions`.

### Keep controllers thin and value-object-first

- **Rule:** 🔴 MUST accept scalar value-object parameters directly in controllers, map domain results to DTOs in controllers, place `[Consumes("application/json")]` per-action on body-bearing endpoints only (never at the class level), avoid JSON `[Produces]`, and add XML doc comments to all public API types and members.
- **Rationale:** Scalar binding and HTTP mapping are presentation concerns; handlers should stay domain-focused, and missing XML docs break builds with CS1591. Class-level `[Consumes("application/json")]` causes `415 Unsupported Media Type` on body-less POSTs such as state-transition triggers (`/orders/{id}/submission`, `/complete`, `/cancel`), because the request has no `Content-Type` header. `[Produces("application/json")]` also rewrites RFC 9457 failures to the wrong media type; use `[ProducesResponseType]` to document responses without changing content negotiation.
- **Correct:**
```csharp
using Mediator;
using Microsoft.AspNetCore.Mvc;
using TodoSample.Api.v2026_03_26.Models;
using TodoSample.Application.Todos;
using TodoSample.Domain;
using Trellis.Asp;
using Trellis.Asp.ApiVersioning;

[ApiController]
[Route("api/[controller]")]
public class TodosController : ControllerBase
{
    private readonly ISender _sender;

    /// <summary>
    /// Constructor.
    /// </summary>
    public TodosController(ISender sender) => _sender = sender;

    /// <summary>
    /// Get a todo item by ID.
    /// </summary>
    [HttpGet("{id}", Name = "Todos_GetById_v2026_03_26")]
    public async ValueTask<ActionResult<TodoResponse>> GetById(TodoId id, CancellationToken cancellationToken) =>
        await _sender.Send(new GetTodoByIdQuery(id), cancellationToken)
            .ToHttpResponseAsync(
                TodoResponse.From,
                opts => opts
                    .WithETag(t => EntityTagValue.Strong(t.ETag))
                    .WithLastModified(t => t.LastModified)
                    .EvaluatePreconditions())
            .AsActionResultAsync<TodoResponse>();

    /// <summary>
    /// Create a new todo item.
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    public ValueTask<ActionResult<TodoResponse>> Create([FromBody] CreateTodoRequest request, CancellationToken cancellationToken) =>
        CreateTodoCommand.TryCreate(request.Title, request.DueDate, request.Tag)
            .BindAsync(command => _sender.Send(command, cancellationToken))
            .ToHttpResponseAsync(
                TodoResponse.From,
                opts => opts
                    .CreatedAtRoute("Todos_GetById_v2026_03_26", t => new Microsoft.AspNetCore.Routing.RouteValueDictionary { ["id"] = (Guid)t.Id })
                    .WithVersionedRoute()
                    .WithETag(t => EntityTagValue.Strong(t.ETag))
                    .WithLastModified(t => t.LastModified))
            .AsActionResultAsync<TodoResponse>();
}
```
- **Incorrect:**
```csharp
// ❌ Class-level [Consumes] returns 415 on body-less POSTs (state-transition triggers).
[ApiController]
[Consumes("application/json")]
[Route("api/[controller]")]
public class TodosController : ControllerBase { /* ... */ }

// ❌ Raw primitives in controller signatures, no DTO mapping, no XML docs.
[HttpGet("{id}")]
public async Task<TodoItem> GetById(Guid id, CancellationToken cancellationToken)
{
    var todoId = TodoId.Create(id);
    return (await _sender.Send(new GetTodoByIdQuery(todoId), cancellationToken)).Value;
}
```
- **Reference:** See `.agentdocs/packages/trellis.core/trellis/trellis-api-asp.md §Endpoint checklist for generated APIs` for the `[Consumes]` placement rule, `.agentdocs/packages/trellis.core/trellis/trellis-api-asp.md §ActionResultExtensions`, `.agentdocs/packages/trellis.core/trellis/trellis-api-asp.md §ActionResultExtensionsAsync`, `.agentdocs/packages/trellis.core/trellis/trellis-api-asp.md §ServiceCollectionExtensions`.

### Require `If-Match` on body-overwriting mutations; honor it on guarded state-transition POSTs

- **Rule:** 🔴 MUST require `If-Match` on endpoints that can silently overwrite a concurrent write — `PUT`, `PATCH`, `DELETE`, body-carrying mutating `POST` endpoints, and non-commutative additive set operations. Parse `ETagHelper.ParseIfMatch(Request)`, carry `EntityTagValue[]? IfMatchETags` on the command, and apply `.RequireETagAsync(command.IfMatchETags)` after the `NotFound` projection and before mutation.
- **Rule:** 🔴 MUST honor a supplied `If-Match` on **body-less state-transition `POST`** endpoints (e.g., `.../approve`, `.../submit`, `.../cancel`, `.../return`). Use the same parsing/command flow with `.OptionalETagAsync(command.IfMatchETags)` before mutation: no header proceeds, a mismatch returns `412` without changing state or metadata, and the domain guard still rejects invalid transitions with `422`. Do not introduce `428` unless the endpoint contract requires the header.
- **Rationale:** A domain guard validates the current state, not the version the client observed. Header requirement is an endpoint policy; honoring a supplied HTTP precondition is not optional (RFC 9110 §§13.1.1 and 13.2.1). Preserve permission/resource authorization before checking preconditions. See Recipe 23's decision table subject to the known erratum above.
- **Correct (body-carrying PUT — `RequireETag`):**
```csharp
// Application/src/Todos/UpdateTodoCommand.cs  (record + handler colocated)
public sealed record UpdateTodoCommand : ICommand<Result<TodoItem>>, IAuthorize
{
    public TodoId TodoId { get; }
    public Title Title { get; }
    public DueDate DueDate { get; }
    public Maybe<Tag> Tag { get; }
    public EntityTagValue[]? IfMatchETags { get; }   // the If-Match precondition this rule is about
    public IReadOnlyList<string> RequiredPermissions { get; } = [Permissions.TodosUpdate];
    // private ctor + static TryCreate(...) omitted — see the colocation rule for the full always-valid command
}

public sealed class UpdateTodoCommandHandler : ICommandHandler<UpdateTodoCommand, Result<TodoItem>>
{
    private readonly ITodoRepository _repository;

    public UpdateTodoCommandHandler(ITodoRepository repository) => _repository = repository;

    // The unit-of-work commits on handler success; there is no repository Save/Update call.
    public async ValueTask<Result<TodoItem>> Handle(UpdateTodoCommand command, CancellationToken cancellationToken) =>
        await _repository.FindByIdAsync(command.TodoId, cancellationToken)
            .ToResultAsync(Error.NotFound.For<TodoItem>(
                id: command.TodoId,
                detail: $"Todo {command.TodoId} not found."))
            .RequireETagAsync(command.IfMatchETags)
            .BindAsync(todo => todo.Update(command.Title, command.DueDate, command.Tag));
}

// Api/src/{version}/Controllers/TodosController.cs — always-valid command via TryCreate; If-Match parsed from the request
[HttpPut("{id}")]
[Consumes("application/json")]
[ProducesResponseType(typeof(TodoResponse), StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
[ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
public ValueTask<ActionResult<TodoResponse>> Update(TodoId id, [FromBody] UpdateTodoRequest request, CancellationToken cancellationToken)
{
    var ifMatchETags = ETagHelper.ParseIfMatch(Request);
    return UpdateTodoCommand.TryCreate(id, request.Title, request.DueDate, request.Tag, ifMatchETags)
        .BindAsync(command => _sender.Send(command, cancellationToken))
        .ToHttpResponseAsync(TodoResponse.From, opts => opts.WithETag(t => EntityTagValue.Strong(t.ETag)))
        .AsActionResultAsync<TodoResponse>();
}
```
- **Correct (body-less state-transition POST — optional `If-Match`, enforced when supplied):**
```csharp
// Application/src/Todos/CompleteTodoCommand.cs  (record + handler colocated)
public sealed record CompleteTodoCommand : ICommand<Result<TodoItem>>, IAuthorize
{
    public TodoId TodoId { get; }
    public EntityTagValue[]? IfMatchETags { get; }

    public IReadOnlyList<string> RequiredPermissions { get; } = [Permissions.TodosComplete];

    private CompleteTodoCommand(TodoId todoId, EntityTagValue[]? ifMatchETags)
    {
        TodoId = todoId;
        IfMatchETags = ifMatchETags;
    }

    public static Result<CompleteTodoCommand> TryCreate(TodoId? todoId, EntityTagValue[]? ifMatchETags = null) =>
        todoId.ToResult(Error.InvalidInput.ForField(
            code: "required",
            field: "id",
            detail: "Todo id is required."))
            .Map(validId => new CompleteTodoCommand(validId, ifMatchETags));
}

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
            .CheckAsync(todo => todo.Complete(_timeProvider));  // state machine guards the transition
}

// Api/src/{version}/Controllers/TodosController.cs
[HttpPost("{id}/complete")]
[ProducesResponseType(typeof(TodoResponse), StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
[ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
public ValueTask<ActionResult<TodoResponse>> Complete(TodoId id, CancellationToken cancellationToken) =>
    CompleteTodoCommand.TryCreate(id, ETagHelper.ParseIfMatch(Request))
        .BindAsync(command => _sender.Send(command, cancellationToken))
        .ToHttpResponseAsync(TodoResponse.From, opts => opts.WithETag(t => EntityTagValue.Strong(t.ETag)))
        .AsActionResultAsync<TodoResponse>();
```
- **Incorrect:** PUT/PATCH/DELETE handler that calls `new UpdateXyzCommand(id, body)` without `ETagHelper.ParseIfMatch(Request)` and omits `.RequireETag(...)`. Returns `200` even when the client supplied a stale (or missing) `If-Match`, silently overwriting a concurrent change.
- **Incorrect:** A guarded-transition handler that ignores a supplied `If-Match` and mutates before evaluating it.
- **Reference:** See `.agentdocs/packages/trellis.core/trellis/trellis-api-cookbook.md` Recipe 23 for the full endpoint-shape decision table; `Application/src/Todos/UpdateTodoCommand.cs`, `Application/src/Todos/CompleteTodoCommand.cs`, `Application/src/Todos/DeleteTodoCommand.cs` and the matching `Api/src/{date}/Controllers/TodosController.cs` for the canonical patterns; `.agentdocs/packages/trellis.core/trellis/trellis-api-core.md §RequireETag` for the framework primitive.

### Use namespace-based API versioning when selected

- **Rule:** When `apiVersioning` is enabled, 🔴 MUST place each API version's controllers in its own `Api/src/{yyyy-MM-dd}/Controllers/` folder with a matching `{ServiceName}.Api.v{yyyy_MM_dd}.Controllers` namespace. Do NOT add `[ApiVersion("...")]` attributes — `VersionByNamespaceConvention` derives the version from the namespace segment.
- **Rationale:** Trellis template controllers are deliberately thin (route binding + `_sender.Send(...)` + response mapping), so duplicating a controller per version is cheaper than maintaining a single shared controller with version-aware projection seams (`HttpContext.RequestedApiVersion` branches, per-version DTO selection, `[MapToApiVersion]` per action). One folder = one version is easier to reason about and impossible to silently break across versions (a v2 edit cannot affect v1 by accident).
- **When to add a new version:** Copy the latest version's `Api/src/{date}/Controllers/` and `Api/src/{date}/Models/` folders to a new `{date}` folder, change the namespace from `v{yyyy_MM_dd}` to the new value everywhere in the copy, then evolve the v2 copy independently — add fields to its `TodoResponse`, change endpoint shapes, etc. Older versions stay frozen.
- **Correct:**
```csharp
// Api/src/2026-03-26/Controllers/TodosController.cs — v1
namespace TodoSample.Api.v2026_03_26.Controllers;
[ApiController]
[Route("api/[controller]")]
public class TodosController : ControllerBase { /* ... */ }

// Api/src/2026-12-01/Controllers/TodosController.cs — v2 (independent copy)
namespace TodoSample.Api.v2026_12_01.Controllers;
[ApiController]
[Route("api/[controller]")]
public class TodosController : ControllerBase { /* same shape; new IsOverdue field on TodoResponse */ }

// Api/src/DependencyInjection.cs
services.AddApiVersioning()
        .AddMvc(options => options.Conventions.Add(new VersionByNamespaceConvention()))
        .AddApiExplorer()
        .AddOpenApi(options => options.Document.AddScalarTransformers());
```
- **Incorrect:**
```csharp
// ❌ [ApiVersion] attribute when the namespace already provides the version.
[ApiController]
[ApiVersion("2026-12-01")]                                                 // ❌ redundant
[Route("api/[controller]")]
public class TodosController : ControllerBase { /* ... */ }

// ❌ Single shared controller that branches on the requested api-version.
[ApiController]
[ApiVersion("2026-03-26")]
[ApiVersion("2026-12-01")]
[Route("api/[controller]")]
public class TodosController : ControllerBase
{
    [HttpGet("{id}")]
    public ActionResult<object> GetById(TodoId id)
    {
        var version = HttpContext.RequestedApiVersion();
        return version >= new ApiVersion(new DateOnly(2026, 12, 1))
            ? (object)v2Projection(todo)                                   // ❌ projection seam
            : (object)v1Projection(todo);
    }
}

// ❌ DI uses attribute-based discovery (no VersionByNamespaceConvention).
services.AddApiVersioning().AddMvc();                                      // ❌
```
- **Reference:** See `Api/src/2026-03-26/` and `Api/src/2026-12-01/` for the two-version reference layout, plus `Api/src/DependencyInjection.cs` for the DI wiring.

### Read the testing reference before writing tests

- **Rule:** 🔴 MUST read `.agentdocs/packages/trellis.core/trellis/trellis-api-testing-reference.md` before writing tests, and use Trellis testing assertions for `Result<T>` and `Maybe<T>`.
- **Rationale:** The testing package already provides assertions, fake repositories, actor providers, and safe unwrapping patterns expected by this template.
- **Correct:**
```csharp
using Trellis.Testing;

result.Should().BeSuccess();
var order = result.Unwrap();
customer.PhoneNumber.Should().HaveValue();
customer.AlternatePhoneNumber.Should().BeNone();
```
- **Incorrect:**
```csharp
result.Value.Should().NotBeNull();
customer.PhoneNumber.HasValue.Should().BeTrue();
customer.AlternatePhoneNumber.HasNoValue.Should().BeTrue();
```
- **Reference:** See `.agentdocs/packages/trellis.core/trellis/trellis-api-testing-reference.md §Usage notes`, `.agentdocs/packages/trellis.core/trellis/trellis-api-testing-reference.md §UnwrapExtensions`.

## Decision Tables

### Modeling decisions

| Scenario | Use | Not |
|---|---|---|
| Expected business failure | `Result<T>` | Exceptions for normal flow |
| Optional value object | `Maybe<T>` | `T?` |
| Optional entity navigation | `T?` | `Maybe<T>` |
| Domain enum-like concept | `RequiredEnum<T>` | C# `enum` |
| Scalar domain concept | `RequiredString<T>`, `RequiredGuid<T>`, `RequiredInt<T>`, `RequiredDecimal<T>`, `RequiredDateTime<T>`, built-in `Trellis.Primitives` | Raw primitives on domain surfaces |
| Reusable domain concept in two contexts | Separate value-object types | One shared primitive and comments |
| Single-currency money | `MonetaryAmount` | `Money` |
| Multi-currency money | `Money` | `MonetaryAmount` |
| Composite value object | `ValueObject` + `Result.Combine(...)` + `GetEqualityComponents()` | Scalar-shaped wrapper with fake `Value` |
| Optional composite value object in EF Core | `partial Maybe<T>` | Manual nullable owned-type plumbing |

### Validation and authorization decisions

| Scenario | Use | Not |
|---|---|---|
| Command construction (required fields **and** cross-field rules) | Private constructor + static `TryCreate(...)` returning `Result<T>` — for **every** command | Public ctor / `new XyzCommand(...)` at the call site; mutable command + later validation |
| Required nullable fields | `value.ToResult(error)`, `Combine` for independent fields, then `Map` using the validated values (TRLS066) | `Result.Ensure(value is not null, error)` followed by `value!`; keep `Result.Ensure` for boolean guards |
| Validation that cannot happen in `TryCreate` | `IValidate.Validate()` returning `IResult` | Late handler-only validation |
| Permission-based authorization | `IAuthorize` | Handler-side permission `if` statements |
| Resource-based authorization | `IAuthorizeResource<TResource>` + loader | Handler-side ownership checks |
| Shared loader by ID | `SharedResourceLoaderById<TResource, TId>` + `IIdentifyResource<TResource, TId>` | Repeating per-command loader code |
| Complex per-command load logic | `ResourceLoaderById<TMessage, TResource, TId>` | Overfitting a shared loader |
| Optional `If-Match` handling | `.OptionalETag(expectedETags)` | Manual ETag comparison |
| Required `If-Match` on body-overwriting mutations (PUT/PATCH/DELETE, body-carrying POST, non-commutative additive ops) | `.RequireETag(expectedETags)` — see critical rule "Require `If-Match` on body-overwriting mutations" and cookbook Recipe 23 | `.OptionalETag(...)` or omitting the check (lost-update race, silent 200) |
| Body-less state-transition POST (e.g., `.../approve`, `.../cancel`, `.../submit`) | `.OptionalETagAsync(command.IfMatchETags)` before the domain guard: no header proceeds, supplied mismatch returns `412` | Ignoring a supplied header; requiring one unless the endpoint contract demands it |

### Handler and controller decisions

| Scenario | Use | Not |
|---|---|---|
| Straight-through handler flow | `Bind` / `BindAsync` / `CheckAsync` / `Map` | Imperative unwrapping |
| Complex branching where chaining harms readability | Short explicit branching that still returns `Result<T>` | Deep nested `if` blocks everywhere |
| Two or more independent async fetches | `Result.ParallelAsync(...).WhenAllAsync()` | Sequential awaits |
| Save that returns `Result<Unit>` | `BindAsync` or `CheckAsync` | `TapAsync` when the save can fail |
| DTO mapping | Controller result mappers | Handler returns DTOs |
| POST create response | `ToCreatedAtActionResult(...)` / `ToCreatedAtActionResultAsync(...)` | `Ok(...)` |
| PUT/PATCH response with `Prefer` and ETag | `ToUpdatedActionResultAsync(...)` | Manual status-code branching |
| Scalar route/query/body binding | Accept Trellis value objects directly | `TryCreate` every scalar in controllers |
| Composite VO request binding | Build with `TryCreate(...).BindAsync(...)` in the controller | Primitive command properties |

### EF Core and query decisions

| Scenario | Use | Not |
|---|---|---|
| Conventions | `ApplyTrellisConventionsFor<TContext>()` (source-generated, preferred) | Reflection `ApplyTrellisConventions(assembly)` fallback unless the context is private/generic/abstract; manual `HasConversion()` / `OwnsOne()` for Trellis-supported types |
| Interceptors | `AddTrellisInterceptors()` | Reimplement timestamp or ETag plumbing |
| Save changes in repositories | `SaveChangesResultUnitAsync()` | Bare `SaveChangesAsync()` |
| Optional lookup | `FirstOrDefaultMaybeAsync(...)` | `FirstOrDefaultAsync(...)` + `null` |
| Required lookup | `FirstOrDefaultResultAsync(..., new Error.NotFound(...))` | Returning `null` or throwing |
| `Maybe<T>` comparisons in LINQ | `WhereLessThan`, `WhereHasValue`, `WhereEquals`, etc. | Direct `Value` access in LINQ |
| Index containing `Maybe<T>` | `HasTrellisIndex(...)` | `HasIndex(...)` |
| Entity configuration placement | `IEntityTypeConfiguration<T>` in Acl | Inline `OnModelCreating` configuration |
## Reference Implementation

Study these files before replacing the Todo sample.

| Pattern | Files |
|---|---|
| Scalar value objects with `RequiredGuid`, `RequiredString`, `RequiredDateTime`, `ValidateAdditional` | `Domain/src/ValueObjects/` |
| `RequiredEnum` smart enum | `Domain/src/TodoStatus.cs` |
| Aggregate with `LazyStateMachine` and `Maybe<T>` partial properties | `Domain/src/Aggregates/TodoItem.cs` |
| Specification with `.And()` composition | `Domain/src/Specifications/OverdueTodoSpecification.cs` |
| Always-valid command with private constructor + `TryCreate` | `Application/src/Todos/UpdateTodoCommand.cs` |
| `Result.Ensure` authorization check | `Application/src/Todos/CompleteTodoCommand.cs` |
| `IAuthorizeResource<T>` with `SharedResourceLoaderById` or `ResourceLoaderById` | `Application/src/Todos/CompleteTodoCommand.cs`, `Acl/src/CompleteTodoResourceLoader.cs` |
| Repository returning `Maybe<T>` | `Application/src/Todos/ITodoRepository.cs` |
| Handlers returning domain types and controller DTO mapping | `Application/src/Todos/`, `Api/src/2026-03-26/Models/TodoResponse.cs` |
| `TimeProvider` for testable time validation | `Application/src/Todos/UpdateTodoCommand.cs` |
| Controller `TryCreate` → `BindAsync` → `Send` flow | `Api/src/2026-03-26/Controllers/TodosController.cs` |
| Domain, Application, and API tests | `Domain/tests/`, `Application/tests/`, `Api/tests/` |

## Architecture and Layout

### Layer dependency matrix

- **Rule:** 🟡 SHOULD keep dependencies flowing inward only.
- **Rationale:** Trellis expects domain purity, application orchestration, Acl persistence adapters, and API presentation boundaries.
- **Correct:**

| Layer | Can depend on | Cannot depend on | Contains |
|---|---|---|---|
| Domain | Trellis packages only (`Results`, `Primitives`, `DDD`, `Stateless`, `Authorization`) | EF Core, ASP.NET Core, Mediator | Aggregates, entities, value objects, domain events, specifications, permission constants |
| Application | Domain, Mediator, `Trellis.Mediator` | ASP.NET Core, EF Core providers | Commands, queries, handlers, repository interfaces |
| Acl | Application, `Trellis.EntityFrameworkCore`, EF Core provider | API types | `DbContext`, entity configurations, repository implementations, migrations, resource loaders |
| Api | Application, Acl, `Trellis.Asp` | Domain persistence implementation details | Controllers/endpoints, DTOs, `Program.cs`, `IActorProvider` |

- **Incorrect:** Let Domain reference EF Core or ASP.NET Core, place repository implementations in Application, or return DTOs from handlers.
- **Reference:** See `.agentdocs/packages/trellis.core/trellis/trellis-api-core.md`, `.agentdocs/packages/trellis.core/trellis/trellis-api-asp.md`, `.agentdocs/packages/trellis.core/trellis/trellis-api-efcore.md`.

> **Why “Acl”?** ACL stands for Anti-Corruption Layer. It adapts external systems (SQL Server, message queues, other services) to the domain model and avoids overloading the word “Infrastructure”.

### Composition root and registration rules

- Use one `Trellis.ServiceDefaults.AddTrellis` in the API root to select ASP, scalar validation,
  ProblemDetails, idempotency, Mediator behaviors, domain events, FluentValidation, resource
  authorization, the Development actor provider and the EF unit of work. Keep DbContexts,
  source-generated Mediator handlers and store registrations
  in their existing layers. Use `app.UseTrellisProblemDetails()` instead of hand-writing the
  trace id, error envelope and Allow-header customization.
- Keep in-memory idempotency restricted to Development. Outside Development, configure the
  Cosmos store through `Idempotency:Cosmos:*` and managed identity; never fall back to memory.
- Enable OTLP only with `OTEL_EXPORTER_OTLP_ENDPOINT`, and Azure Monitor only with
  `APPLICATIONINSIGHTS_CONNECTION_STRING`. Azure profiles include a deployment guide for the matching resources.

- **Rule:** 🔴 MUST keep repository interfaces in Application, implementations in Acl, one `DependencyInjection.cs` per layer, `IActorProvider` as singleton in Api, and `TimeProvider.System` as a singleton in Application.
- **Rationale:** Trellis pipeline behaviors are singleton-based, and ASP.NET Core does not auto-register `TimeProvider`.
- **Correct:**
```csharp
using Microsoft.Extensions.DependencyInjection;

services.AddSingleton(TimeProvider.System);
services.AddCachingActorProvider<HttpActorProvider>();
```
- **Incorrect:**
```csharp
services.AddScoped<IActorProvider, HttpActorProvider>();
```
- **Reference:** See `.agentdocs/packages/trellis.core/trellis/trellis-api-authorization.md`, `.agentdocs/packages/trellis.core/trellis/trellis-api-cookbook.md`, `.agentdocs/packages/trellis.core/trellis/trellis-api-asp.md`.

> **`CachingActorProvider`:** When you need synchronous actor access after the async pipeline resolves it, use `AddCachingActorProvider<T>()`. It caches the actor per request in `HttpContext.Items` and prevents a singleton pipeline from depending on a scoped provider.

### Project layout

- **Rule:** 🟡 SHOULD preserve the template structure and only add code where the template expects it.
- **Rationale:** The solution, package management, and test props are already preconfigured.
- **Correct:**
```text
{ServiceName}/
├── {ServiceName}.slnx
├── Directory.Build.props          ← DO NOT MODIFY
├── Directory.Packages.props       ← ADD new packages here (versions only)
├── global.json                    ← DO NOT MODIFY
├── build/
│   └── test.props                 ← DO NOT MODIFY
├── .config/
│   └── dotnet-tools.json          ← pins Trellis.AgentDocs
├── .agentdocs/
│   ├── README.md                  ← START HERE: project-aware guidance index
│   ├── policy.json                ← approved guidance publishers
│   └── packages/
│       └── trellis.core/trellis/  ← version-aligned Trellis references
├── AGENTS.md                      ← THIS FILE + managed AgentDocs pointer
├── .github/
│   └── copilot-instructions.md    ← Copilot compatibility pointer
├── Domain/
│   ├── src/
│   │   └── Domain.csproj
│   └── tests/
│       └── Domain.Tests.csproj
├── Application/
│   ├── src/
│   │   └── Application.csproj
│   └── tests/
│       └── Application.Tests.csproj
├── Acl/
│   ├── src/
│   │   └── AntiCorruptionLayer.csproj
│   └── tests/
│       └── AntiCorruptionLayer.Tests.csproj
└── Api/
    ├── src/
    │   └── Api.csproj
    └── tests/
        └── Api.Tests.csproj
```
- **Incorrect:** Recreate `Directory.Build.props`, put package versions in `.csproj`, or create alternative folder conventions that bypass the template.
- **Reference:** See the template tree under `template/`.

> **NuGet packages:** Add `<PackageVersion>` to `Directory.Packages.props`, then add `<PackageReference>` without a version in the relevant `.csproj`.

> **Maintaining AgentDocs:** From the Git root, run `dotnet tool restore`, `dotnet restore`, `dotnet tool run agentdocs sync`, and `dotnet tool run agentdocs check --strict` after generating the project or upgrading packages. Commit the tool manifest, managed pointers, policy, and `.agentdocs/` with the package update. The application does not require this optional tool to build or run.

### HTTP request documentation files

- **Rule:** 🟡 SHOULD replace `Api/src/api.http` with end-to-end requests for every endpoint and keep complex header values in `Api/src/http-client.env.json` as escaped JSON strings.
- **Rationale:** The `.http` file is living API documentation, and the HTTP client only supports scalar variable substitution.
- **Correct:**
```json
{
  "dev": {
    "host": "https://localhost:7011",
    "apiVersion": "2026-11-12",
    "adminActor": "{\"Id\":\"admin-1\",\"Permissions\":[\"customers:create\",\"products:create\"]}",
    "userActor": "{\"Id\":\"user-1\",\"Permissions\":[\"orders:create\",\"orders:read\"]}"
  }
}
```
- **Incorrect:** Put nested JSON directly in `.http` variables or let `host` drift from `Properties/launchSettings.json`.
- **Reference:** See `Api/src/api.http`, `Api/src/http-client.env.json`, and `Api/src/Properties/launchSettings.json`.

## Implementation Order and Build Checkpoints

### Build between layers

- **Rule:** 🔴 MUST build after each layer because generated code appears only after compilation.
- **Rationale:** The `MaybePartialPropertyGenerator` emits `_camelCase` backing fields used later by EF Core configuration and query helpers.
- **Correct:**
```text
1. Domain/src — implement value objects, aggregates, entities, events, specifications, permissions. Then run dotnet build.
2. Application/src — implement repository interfaces, commands, queries, handlers. Then run dotnet build.
3. Acl/src — implement DbContext, entity configurations, repositories, resource loaders. Then run dotnet build.
4. Api/src — implement controllers, DTOs, Program.cs, IActorProvider. Then run dotnet build.
5. Tests — implement Domain.Tests, Application.Tests, Api.Tests. Then run dotnet test.
```
- **Incorrect:** Depend on `_submittedAt` or generated mediator code before the earlier projects have been built once.
- **Reference:** See `.agentdocs/packages/trellis.core/trellis/trellis-api-efcore.md` and `.agentdocs/packages/trellis.core/trellis/trellis-api-mediator.md`.