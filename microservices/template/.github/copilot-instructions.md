# Copilot Instructions — Building Microservices with Trellis

This template scaffolds a **multi-tenant microservices topology** on the Trellis framework for .NET 10, orchestrated by .NET Aspire. It ships two reference services — **Members** and **Projects** — behind a **Gateway**, communicating asynchronously through integration events. Build new services and features by following the patterns the reference services already demonstrate.

## 🔴 Before Writing Code — Read the API References

**STOP. Do not write or generate any code until you have read the API reference files relevant to your task.** These files document the exact method signatures, overloads, conventions, and EF Core mapping rules. Guessing based on type names produces code that compiles but fails at runtime (e.g., adding explicit EF `Property()` configuration on types that Trellis conventions already handle).

For a typical service using aggregates, EF Core, authorization, and the eventing plane, read at least: `trellis-api-core.md`, `trellis-api-primitives.md`, `trellis-api-efcore.md`, `trellis-api-asp.md`, `trellis-api-authorization.md`, `trellis-api-mediator.md`, `trellis-api-efcore-outbox.md`, `trellis-api-efcore-inbox.md`, `trellis-api-microservices-abstractions.md`, `trellis-api-microservices-cookbook.md`, and `trellis-api-testing-reference.md`.

**Reference docs are authoritative.** If anything in this file conflicts with one of the `trellis-api-*.md` reference files, the reference file wins — those files are auto-synced from package metadata (`dotnet build /t:TrellisSyncApiReference`) and reflect the current framework surface. This file is curated guidance that can drift. Please file any contradiction as feedback.

| When working on... | Read first |
|---|---|
| `Result<T>`, `Maybe<T>`, `Error`, `Bind`, `Map`, `Tap`, `Ensure`, `Combine` | `.github/trellis-api-core.md` |
| Aggregates, entities, value objects, specifications, ETag checks | `.github/trellis-api-core.md` |
| `RequiredString<T>`, `RequiredGuid<T>`, `RequiredEnum<T>`, built-in primitives | `.github/trellis-api-primitives.md` |
| Minimal-API result mappers (`ToHttpResponse`/`ToHttpResponseAsync`), `EntityTagValue`, scalar binding/validation | `.github/trellis-api-asp.md` |
| Minimal-API versioning (`WithVersionedRoute`, version sets) | `.github/trellis-api-asp-apiversioning.md` |
| EF Core conventions, interceptors, `HasTrellisIndex`, `FirstOrDefaultMaybeAsync` | `.github/trellis-api-efcore.md` |
| Transactional **outbox** (domain → integration events, relay, dead-letter) | `.github/trellis-api-efcore-outbox.md` |
| Transactional **inbox** (idempotent consume, consumer checkpoints) | `.github/trellis-api-efcore-inbox.md` |
| Integration-event contracts, collector, publisher/handler SPIs | `.github/trellis-api-microservices-abstractions.md` |
| Cross-service recipes (translator, consumer, read model, deterministic ids) | `.github/trellis-api-microservices-cookbook.md` |
| Actor-based authorization, `IAuthorize`, resource authorization | `.github/trellis-api-authorization.md` |
| Internal-JWT actor provider + gateway minting | `.github/trellis-api-internal-jwt.md` |
| YARP gateway / actor forwarding | `.github/trellis-api-yarp.md` |
| Mediator pipeline behaviors | `.github/trellis-api-mediator.md` |
| Service-level indicators (SLI) | `.github/trellis-api-sli.md`, `.github/trellis-api-sli-asp.md` |
| Testing helpers, `FakeRepository`, `TestActorProvider`, assertions, `Unwrap()` | `.github/trellis-api-testing-reference.md` |
| Analyzer diagnostics `TRLS001`–`TRLS0xx` and generator diagnostics | `.github/trellis-api-analyzers.md` |
| Scalar vs composite value-object classification | `.github/trellis-value-object-taxonomy.md` |

## Critical Rules

### Study the reference services first

- **Rule:** 🔴 MUST read the Members and Projects services before adding or replacing one.
- **Rationale:** The shipped services demonstrate the exact Trellis patterns this template expects — the endpoint-folder layout, server-derived tenancy, the outbox/inbox eventing plane, and resource authorization.
- **Correct:** Inspect `Members/` (the write-side data plane) and `Projects/` (the read-side consumer) before generating your own service.
- **Incorrect:** Recreate the topology from scratch without checking the working services.
- **Reference:** See `Members/{Domain,Application,Acl,Api}/src/` and `Projects/{Domain,Application,Acl,Api}/src/`.

### Put routes in a `*Endpoints.cs` extension; hoist shared conventions onto the group

- **Rule:** 🔴 MUST define HTTP routes in a dedicated `{Resource}Endpoints.cs` static class (one per resource) exposing a `Map{Resource}Endpoints(this IEndpointRouteBuilder)` extension — never inline in `Program.cs`. Create the routes under a single `MapGroup` and declare the conventions shared by **every** endpoint (the API version set, `MapToApiVersion`, `RequireAuthorization`, `AddServiceLevelIndicator`, `WithTags`) **once on the group**. Leave only genuinely per-endpoint concerns on each route — the route name, `WithScalarValueValidation()` (body endpoints), idempotency, `CreatedAtRoute(...).WithVersionedRoute()`, `WithETag`/`WithLastModified`.
- **Rationale:** Hoisting the cross-cutting conventions onto the group removes per-route repetition, keeps `Program.cs` focused on the trust boundary + DI, and makes the version/auth/SLI story uniform and hard to get wrong. Each endpoint reads as just its route + the `mediator.Send(...)` mapping.
- **Correct:**
```csharp
namespace ProjectTrackerTemplate.Members.Api;

using Asp.Versioning;
using Asp.Versioning.Builder;
using Mediator;
using ProjectTrackerTemplate.Members.Application;
using ProjectTrackerTemplate.Members.Domain;
using Trellis;
using Trellis.Asp;
using Trellis.Asp.ApiVersioning;
using Trellis.Asp.Idempotency;
using Trellis.Primitives;
using Trellis.ServiceLevelIndicators;

public static class MemberEndpoints
{
    private static readonly ApiVersion V20260326 = new(new DateOnly(2026, 3, 26));

    public static IEndpointRouteBuilder MapMemberEndpoints(this IEndpointRouteBuilder app)
    {
        ApiVersionSet versionSet = app.NewApiVersionSet("Members")
            .HasApiVersion(V20260326)
            .ReportApiVersions()
            .Build();

        // Conventions shared by EVERY endpoint are declared once on the group.
        var members = app.MapGroup("/api/members")
            .WithApiVersionSet(versionSet)
            .WithTags("Members")
            .MapToApiVersion(V20260326)
            .RequireAuthorization()
            .AddServiceLevelIndicator();

        members.MapGet("/{id}", (MemberId id, IMediator mediator, CancellationToken ct) =>
            mediator.Send(new GetMemberQuery(id), ct)
                .ToHttpResponseAsync(
                    MemberResponse.From,
                    opts => opts
                        .WithETag(m => EntityTagValue.Strong(m.ETag))
                        .WithLastModified(m => m.LastModified)))
            .WithName("Members_GetById");

        members.MapPost("/", (InviteMemberRequest body, IMediator mediator, CancellationToken ct) =>
                mediator.Send(new InviteMemberCommand(body.Email, body.Role), ct)
                    .ToHttpResponseAsync(
                        MemberResponse.From,
                        opts => opts
                            .CreatedAtRoute("Members_GetById", m => m.Id)
                            .WithVersionedRoute()
                            .WithETag(m => EntityTagValue.Strong(m.ETag))
                            .WithLastModified(m => m.LastModified)))
            .WithScalarValueValidation()
            .WithMetadata(new IdempotentAttribute());

        return app;
    }
}
```
- **Incorrect:**
```csharp
// ❌ Routes inline in Program.cs, conventions repeated per endpoint.
app.MapGet("/api/members/{id}", handler).RequireAuthorization().AddServiceLevelIndicator().MapToApiVersion(v);
app.MapPost("/api/members", handler).RequireAuthorization().AddServiceLevelIndicator().MapToApiVersion(v);
```
- **Reference:** See `Members/Api/src/MemberEndpoints.cs`, `Projects/Api/src/ProjectEndpoints.cs`, `Projects/Api/src/TeamEndpoints.cs`, and the hoisting note in `.github/trellis-api-asp.md`.

### Declare permissions as constants in the Domain layer

- **Rule:** 🔴 MUST declare each service's permission scopes as `public const string` members of a `public static class Permissions` in that service's **Domain** project, and reference them from commands/queries via `IAuthorize.RequiredPermissions => [Permissions.X]`. Never hard-code permission strings at the call site, and never put the constants in the Application or Api layer.
- **Rationale:** Permissions are a domain vocabulary (what the service allows), so they belong with the domain. Centralizing them as typed constants prevents string drift between the command that requires a permission and the policy/seed that grants it, and keeps the authorization surface auditable in one place per service.
- **Correct:**
```csharp
// Members/Domain/src/Permissions.cs
namespace ProjectTrackerTemplate.Members.Domain;

public static class Permissions
{
    public const string MembersRead = "members:read";
    public const string MembersInvite = "members:invite";
}

// Members/Application/src/InviteMemberCommand.cs
public sealed record InviteMemberCommand(EmailAddress Email, Role Role)
    : ICommand<Result<Member>>, IAuthorize
{
    public IReadOnlyList<string> RequiredPermissions => [Permissions.MembersInvite];
}
```
- **Incorrect:**
```csharp
// ❌ Magic string at the call site, no shared constant.
public IReadOnlyList<string> RequiredPermissions => ["members:invite"];

// ❌ Permission constants living in Application or Api (wrong layer).
```
- **Reference:** See `Members/Domain/src/Permissions.cs`, `Projects/Domain/src/Permissions.cs`, and their consumers in `Members/Application/src/`, `Projects/Application/src/`.

### Keep each command/query and its handler in one file

- **Rule:** 🔴 MUST colocate a command/query record and its handler class in the **same file**, named after the message (e.g. `Members/Application/src/InviteMemberCommand.cs` contains both `InviteMemberCommand` and `InviteMemberHandler`). Do not split the handler into a separate `*Handler.cs`.
- **Rationale:** A command and its handler are one feature slice; reading or changing the behaviour means reading both. One file per feature keeps the slice cohesive, makes the message-to-handler mapping obvious, and avoids a parallel folder of handlers that drifts out of step with its messages.
- **Correct:**
```csharp
// Members/Application/src/InviteMemberCommand.cs — record + handler together
public sealed record InviteMemberCommand(EmailAddress Email, Role Role)
    : ICommand<Result<Member>>, IAuthorize
{
    public IReadOnlyList<string> RequiredPermissions => [Permissions.MembersInvite];
}

public sealed class InviteMemberHandler(
    IMemberRepository repository, IActorProvider actorProvider, TimeProvider timeProvider)
    : ICommandHandler<InviteMemberCommand, Result<Member>>
{
    public async ValueTask<Result<Member>> Handle(InviteMemberCommand command, CancellationToken cancellationToken)
    {
        // Tenant is server-derived from the actor, never taken from the request body.
        var tenantId = await actorProvider.GetCurrentTenantIdAsync(cancellationToken);
        var localPart = command.Email.Value.Split('@')[0];

        return await MemberId.TryCreate($"{tenantId.Value}-{localPart}")
            .EnsureAsync(
                async memberId => !await repository.ExistsAsync(memberId, cancellationToken),
                memberId => Error.Conflict.For<Member>(memberId, "members.duplicate", "A member with this id already exists in this tenant."))
            .MapAsync(memberId => Member.Invite(memberId, tenantId, command.Email, command.Role, timeProvider))
            .TapAsync(repository.Add);
    }
}
```
- **Incorrect:** `Members/Application/src/InviteMemberCommand.cs` holding only the record, with `InviteMemberHandler.cs` in a separate `Handlers/` folder.
- **Reference:** See `Members/Application/src/InviteMemberCommand.cs`, `Members/Application/src/GetMemberQuery.cs`, `Projects/Application/src/ListProjectsQuery.cs`, `Projects/Application/src/UpdateProjectCommand.cs`, `Projects/Application/src/GetProjectQuery.cs`.

### Keep `Program.cs` thin; register each layer with an `Add{Service}{Layer}` extension

- **Rule:** 🔴 MUST keep each service host's `Program.cs` focused on the trust boundary (authentication, the internal-JWT actor provider, validation/idempotency middleware, SLI middleware) and a single call to each layer's DI extension — `Add{Service}Application()` (in the Application project) and `Add{Service}Acl(builder)` (in the Acl project) — then `app.Map{Resource}Endpoints()`. Do not register handlers, repositories, DbContexts, or the eventing plane inline in `Program.cs`.
- **Rationale:** The assembly that owns a layer's types owns its wiring — so `AddDomainEventDispatch` scans the Application assembly where the handlers actually live, and the Acl assembly owns the EF/outbox/inbox registrations. A thin host stays readable and a new service is wired with two `Add…` calls.
- **Correct:**
```csharp
// Members/Application/src/DependencyInjection.cs
public static IServiceCollection AddMembersApplication(this IServiceCollection services)
{
    services.AddSingleton(TimeProvider.System);
    services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
    services.AddTrellisBehaviors();
    services.AddDomainEventDispatch(typeof(MemberInvitedTranslator).Assembly);
    services.AddIntegrationEventDispatch();
    return services;
}

// Members/Api/src/Program.cs (excerpt)
builder.Services.AddMembersApplication();
builder.AddMembersAcl();
// ...
app.MapMemberEndpoints();
app.MapDefaultEndpoints();
```
- **Incorrect:** `Program.cs` calling `AddScoped<IMemberRepository, EfMemberRepository>()`, `AddDbContext<…>()`, `AddTrellisOutbox<…>()`, and registering handlers directly.
- **Reference:** See `Members/Application/src/DependencyInjection.cs`, `Members/Acl/src/DependencyInjection.cs`, `Members/Api/src/Program.cs`, and the Projects equivalents.

### Treat errors and optional values as explicit types

- **Rule:** 🔴 MUST use `Result<T>` for expected failures and `Maybe<T>` for optional values. Never throw for business logic; never use `try/catch` in Domain or Application layers for expected outcomes.
- **Rationale:** Trellis relies on Railway Oriented Programming; exceptions for expected paths break the pipeline and reduce testability.
- **Exceptions are still for the _exceptional_.** "Never throw for an **expected** outcome" (validation, not-found, conflict, forbidden, optional absence — model these as `Result<T>` / `Maybe<T>`), **not** "never throw at all". `throw` remains correct for unrecoverable faults (API misuse, failed startup/configuration checks, infrastructure errors). For internal "shouldn't happen" faults you may also return `Error.Unexpected(reasonCode, faultId?)`.
- **Correct:**
```csharp
public static Result<MemberId> TryCreate(string value) =>
    string.IsNullOrWhiteSpace(value)
        ? Result.Fail<MemberId>(Error.InvalidInput.ForField("id", "required", "Id is required."))
        : Result.Ok(new MemberId(value));
```
- **Incorrect:** `if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Id is required.");`
- **Reference:** See `.github/trellis-api-core.md`.

### Keep handlers on the ROP track and return `ValueTask` via `.AsValueTask()`

- **Rule:** 🔴 MUST compose handler flows with `Ensure`/`EnsureAsync`, `Map`/`MapAsync`, `Bind`/`BindAsync`, `Tap`/`TapAsync`, and `RequireETag`. For a synchronous result in a `ValueTask`-returning handler, end the chain with `.AsValueTask()` — do not wrap in `new ValueTask<Result<T>>(...)` or `new(...)`. A resource-authorized handler reads the already-loaded aggregate via `_authorized.GetRequiredResource()` and never re-fetches it.
- **Rationale:** ROP chains preserve failure propagation and keep the success path explicit; `AsValueTask()` is the idiomatic, allocation-aware bridge; re-fetching a resource the pipeline already loaded defeats the load-once guarantee.
- **Correct:**
```csharp
public sealed partial class UpdateProjectHandler(IAuthorizedResource<UpdateProjectCommand, Project> authorized)
    : ICommandHandler<UpdateProjectCommand, Result<Project>>
{
    public ValueTask<Result<Project>> Handle(UpdateProjectCommand command, CancellationToken cancellationToken) =>
        Result.Ok(authorized.GetRequiredResource())
            .RequireETag(command.IfMatchETags)
            .Tap(project => project.Update(command.Title, command.Description))
            .AsValueTask();
}
```
- **Incorrect:**
```csharp
// ❌ Imperative unwrap + re-fetch + ValueTask ctor wrapping.
var maybe = await _repository.FindByIdAsync(command.Id, ct);
if (maybe.HasNoValue) return Error.NotFound.For<Project>(command.Id);
maybe.Value.Update(command.Title, command.Description);
return new(Result.Ok(maybe.Value));
```
- **Reference:** See `Projects/Application/src/UpdateProjectCommand.cs`, `Projects/Application/src/GetProjectQuery.cs`, `.github/trellis-api-core.md`, `.github/trellis-api-mediator.md`.

### Eliminate primitive obsession; model domain enums as `RequiredEnum<T>`

- **Rule:** 🔴 MUST expose value objects on aggregates, entities, commands, and public domain methods — never raw `Guid`/`string`/`int`/`decimal` for domain concepts — and model closed sets as `RequiredEnum<T>` partial classes, not C# `enum`. Value objects convert implicitly to their primitive, so a response DTO mapper passes the value object directly (no `.Value`).
- **Rationale:** Trellis models validity at the type level; primitive-based domain APIs reintroduce invalid states. `RequiredEnum<T>` adds validation, JSON/EF conversion, and LINQ support a C# `enum` cannot.
- **Correct:**
```csharp
public sealed partial class MemberId : RequiredString<MemberId>;

public partial class Role : RequiredEnum<Role>
{
    [EnumValue("owner")] public static readonly Role Owner = new();
    [EnumValue("contributor")] public static readonly Role Contributor = new();
}

internal sealed record MemberResponse(string Id, string TenantId, string Email, string Role)
{
    public static MemberResponse From(Member m) => new(m.Id, m.TenantId, m.Email, m.Role);
}
```
- **Incorrect:** `public sealed record InviteMemberCommand(string Email, string Role);` or `public enum Role { Owner, Member }`.
- **Reference:** See `Members/Domain/src/MemberId.cs`, `Members/Domain/src/Role.cs`, `.github/trellis-api-primitives.md`, `.github/trellis-value-object-taxonomy.md`.

### Derive tenant from the actor, never from the request body

- **Rule:** 🔴 MUST obtain the caller's tenant in tenant-scoped handlers via the service's `IActorProvider.GetCurrentTenantIdAsync(...)` extension, and scope every write/read to it server-side. The wire format MUST NOT accept a `tenant_id` parameter.
- **Rationale:** The gateway projects `tenant_id` as a required actor attribute on the internal JWT; trusting a body-supplied tenant would let a caller act across tenants. Server-derivation closes that hole by construction.
- **Correct:**
```csharp
var tenantId = await actorProvider.GetCurrentTenantIdAsync(cancellationToken);
var projects = await repository.ListByTenantAsync(tenantId, cancellationToken);
```
- **Incorrect:** A `ListProjectsQuery(TenantId TenantId)` whose tenant comes from the request, or re-reading the JWT/claims inside the handler.
- **Reference:** See `Members/Application/src/ActorProviderExtensions.cs`, `Projects/Application/src/ActorProviderExtensions.cs`, `Members/Application/src/InviteMemberCommand.cs`, `.github/trellis-api-internal-jwt.md`. The extension is **per service** because `SharedKernel` deliberately does not reference `Trellis.Authorization`.

### Communicate across services asynchronously through integration events

- **Rule:** 🔴 MUST NOT make synchronous service-to-service calls for state another service owns. A producer raises a **domain event**, the **outbox** captures it in the same transaction as the aggregate, a **translator** maps it to a published-language **integration event**, and a consumer service ingests it through its **inbox** and projects it into a local read model. Consumers answer from their own store; they never call the producer at request time.
- **Rationale:** Synchronous cross-service calls couple availability and latency and create distributed-transaction problems. The outbox/inbox pattern gives at-least-once delivery with transactional capture and idempotent consume, so each service stays independently deployable and resilient.
- **Reference:** See the next rule for the full shape; `Members/*` (producer) and `Projects/*` (consumer) for the working flow; `.github/trellis-api-efcore-outbox.md`, `.github/trellis-api-efcore-inbox.md`, `.github/trellis-api-microservices-cookbook.md`.

### Publish a stable integration-event contract; consume it idempotently

- **Rule:** 🔴 MUST express each cross-service contract as a **primitives-only** `IIntegrationEvent` record carrying a `const string MessageType` discriminator (versioned, e.g. `"...v1"`); produce it from a domain event via an `IDomainEventHandler<TDomainEvent>` **translator** that `Add`s it to the `IIntegrationEventCollector`; give every copy of one logical event a **deterministic** id (so redeliveries dedupe on business identity); and consume it through the **inbox** with an `AddIntegrationEventHandler<TEvent, THandler>` that stages an idempotent projection. Never put a value object or `Maybe<T>` on the wire contract.
- **Rationale:** A primitives-only, versioned contract decouples consumers from the producer's internal types; the deterministic id + inbox make at-least-once delivery safe (the consumer collapses redeliveries to a single effect); the translator keeps the published language separate from the internal domain event.
- **Correct:**
```csharp
// SharedKernel/src/MemberInvitedIntegrationEvent.cs — published language (primitives only)
public sealed record MemberInvitedIntegrationEvent(
    Guid EventId, string TenantId, string MemberId, string Role, DateTimeOffset OccurredAt) : IIntegrationEvent
{
    public const string MessageType = "projecttracker.members.member-invited.v1";
}

// Members/Application/src/MemberInvitedTranslator.cs — domain event -> contract
internal sealed class MemberInvitedTranslator(IIntegrationEventCollector collector)
    : IDomainEventHandler<MemberInvited>
{
    public ValueTask HandleAsync(MemberInvited e, CancellationToken ct)
    {
        collector.Add(new MemberInvitedIntegrationEvent(
            DeterministicEventId.ForMember(e.MemberId), e.TenantId, e.MemberId, e.Role, e.OccurredAt));
        return ValueTask.CompletedTask;
    }
}

// Projects/Acl/src/DependencyInjection.cs — consumer side (inbox + handler + transport)
services.AddTrellisInbox<ProjectsDbContext>(o => o.ConsumerId = "projects");
services.AddIntegrationEventHandler<MemberInvitedIntegrationEvent, MemberInvitedHandler>();
services.AddHostedService<MemberEventsConsumer>();
```
- **Incorrect:** Putting `MemberId`/`TenantId` value objects on the contract, publishing the raw domain event, generating a random per-message id (defeating dedup), or projecting in the consumer without the inbox (double-apply on redelivery).
- **Reference:** See `SharedKernel/src/MemberInvitedIntegrationEvent.cs`, `Members/Application/src/MemberInvitedTranslator.cs`, `Members/Application/src/DeterministicEventId.cs`, `Members/Acl/src/ServiceBusIntegrationEventPublisher.cs`, `Projects/Acl/src/MemberEventsConsumer.cs`, `Projects/Acl/src/MemberInvitedHandler.cs`.

### Follow Trellis EF Core conventions; repositories stage, the unit of work commits

- **Rule:** 🔴 MUST configure each `DbContext` with `ApplyTrellisConventionsFor<TContext>()` + `AddTrellisInterceptors()`, map the outbox/inbox with `AddTrellisOutbox()` / `AddTrellisInbox()` in `OnModelCreating`, and implement repositories as `RepositoryBase<TAggregate, TId>` that only **stage** changes (`Add`/`Update`/`Remove`) and return `Maybe<T>` from lookups. The handler does NOT call `SaveChanges` — `AddTrellisUnitOfWork<TContext>()` commits on command-handler success.
- **Rationale:** Trellis persistence relies on conventions, interceptors (ETag + timestamps + value-object/`Maybe` query rewriting), and a pipeline-managed unit of work; manual EF patterns or explicit `SaveChangesAsync` calls silently break mapping, concurrency, or the outbox's same-transaction capture.
- **Correct:**
```csharp
// Acl DbContext
protected override void ConfigureConventions(ModelConfigurationBuilder b) =>
    b.ApplyTrellisConventionsFor<MembersDbContext>();
protected override void OnModelCreating(ModelBuilder b)
{
    b.ApplyConfigurationsFromAssembly(typeof(MembersDbContext).Assembly);
    b.AddTrellisOutbox();
}

// Acl DI
builder.AddSqlServerDbContext<MembersDbContext>("membersdb",
    configureDbContextOptions: o => o.AddTrellisInterceptors().AddTrellisOutboxInterceptor());
services.AddScoped<IMemberRepository, EfMemberRepository>();
services.AddTrellisUnitOfWork<MembersDbContext>();
services.AddTrellisOutbox<MembersDbContext>();
```
- **Incorrect:** Calling `SaveChangesAsync()` in a handler/repository, hand-writing `HasConversion()`/`OwnsOne()` for Trellis-supported value objects, or returning `null`/`Result` instead of `Maybe<T>` from a lookup.
- **Reference:** See `Members/Acl/src/MembersDbContext.cs`, `Members/Acl/src/EfMemberRepository.cs`, `Members/Acl/src/DependencyInjection.cs`, `Projects/Acl/src/ProjectsDbContext.cs`, `.github/trellis-api-efcore.md`.

### Authorize with `IAuthorize` and `IAuthorizeResource<T>`; load resources once

- **Rule:** 🔴 MUST use `IAuthorize` for static permission gates and `IAuthorizeResource<TResource>` + `IIdentifyResource<TResource, TId>` for per-resource checks, backed by a `SharedResourceLoaderById<TResource, TId>`. The loader runs once at the pipeline boundary and the handler reads the result via `IAuthorizedResource<TCommand, TResource>` — handlers do not re-check ownership or re-load. Register both with `AddResourceAuthorization(...)` in the Acl layer; opt sensitive resources into existence-hiding with `HideExistence<T>()` (cross-tenant failures project to 404, not 403).
- **Rationale:** Centralizing authorization in the pipeline keeps handlers domain-focused, guarantees the resource is loaded exactly once, and makes the 403-vs-404 disclosure decision explicit per resource type.
- **Correct:**
```csharp
public sealed record UpdateProjectCommand(ProjectId Id, ProjectTitle Title, ProjectDescription Description, EntityTagValue[]? IfMatchETags)
    : ICommand<Result<Project>>, IAuthorize, IAuthorizeResource<Project>, IIdentifyResource<Project, ProjectId>
{
    public IReadOnlyList<string> RequiredPermissions => [Permissions.ProjectsWrite];
    public ProjectId GetResourceId() => Id;
    public Trellis.IResult Authorize(Actor actor, Project resource) =>
        Result.Ensure(
            actor.TryGetAttribute<TenantId>("tenant_id", out var t) && t == resource.TenantId,
            Error.Forbidden.For<Project>("projects.cross_tenant", resource.Id, "Cross-tenant project access is not permitted."))
        .Ensure(
            _ => string.Equals(resource.OwnerId, actor.Id.Value, StringComparison.Ordinal),
            Error.Forbidden.For<Project>("projects.not_owner", resource.Id, "Only the project's owner can edit it."));
}
```
- **Incorrect:** Ownership/tenant `if` checks inside the handler, or loading the aggregate a second time in the handler.
- **Reference:** See `Projects/Application/src/UpdateProjectCommand.cs`, `Projects/Application/src/GetProjectQuery.cs`, `Projects/Acl/src/ProjectResourceLoader.cs`, `Members/Acl/src/DependencyInjection.cs` (`HideExistence<Member>()`), `.github/trellis-api-authorization.md`.

### Require `If-Match` on body-overwriting mutations

- **Rule:** 🔴 MUST wire `If-Match` precondition checking on endpoints whose body can silently overwrite a concurrent write (`PUT`/`PATCH`/`DELETE`, body-carrying mutating `POST`). The command carries `EntityTagValue[]? IfMatchETags`, the endpoint passes the parsed `If-Match`, and the handler chain includes `.RequireETag(command.IfMatchETags)` before the mutation. Body-less guarded state transitions do not need it — the domain guard is the precondition.
- **Rationale:** Skipping the precondition lets concurrent clients silently overwrite each other (lost-update race). Trellis aggregates carry a strong ETag for exactly this check.
- **Correct:** `Result.Ok(authorized.GetRequiredResource()).RequireETag(command.IfMatchETags).Tap(p => p.Update(...))` — see `Projects/Application/src/UpdateProjectCommand.cs`.
- **Incorrect:** An update handler that omits `.RequireETag(...)` and returns `200` even for a stale/missing `If-Match`.
- **Reference:** See `.github/trellis-api-core.md` §RequireETag, `.github/trellis-api-asp.md`.

### Version minimal-API routes with a version set and `WithVersionedRoute()`

- **Rule:** 🔴 MUST bind each endpoint group to an `ApiVersionSet` (`NewApiVersionSet(...).HasApiVersion(...).Build()`) and `MapToApiVersion(...)` on the group, and on any `CreatedAtRoute`/`WithLocation` chain include the version by calling `.WithVersionedRoute()`. Omitting it produces `Location` headers that drop `api-version` and 404 on dereference (analyzer `TRLS023`).
- **Rationale:** Query-string API versioning means a generated `Location` without the version points at a route that cannot be resolved; `WithVersionedRoute()` injects the active version automatically.
- **Correct:** `.CreatedAtRoute("Members_GetById", m => m.Id).WithVersionedRoute()` — see `Members/Api/src/MemberEndpoints.cs`.
- **Incorrect:** `.CreatedAtRoute("Members_GetById", m => m.Id)` with no `.WithVersionedRoute()` under query-string versioning.
- **Reference:** See `.github/trellis-api-asp-apiversioning.md`, `.github/trellis-api-analyzers.md` (TRLS023).

### Read the testing reference before writing tests

- **Rule:** 🔴 MUST read `.github/trellis-api-testing-reference.md` before writing tests and use Trellis assertions (`Should().BeSuccess()`, `Unwrap()`, `Should().HaveValue()`/`BeNone()`). The cross-service eventing path is covered by an in-memory broker end-to-end test, not by calling the real Service Bus.
- **Rationale:** The testing package already provides assertions, fake repositories, actor providers, and safe unwrapping; the eventing test proves outbox → broker → inbox → read model hermetically.
- **Correct:** `result.Should().BeSuccess(); var member = result.Unwrap();`
- **Incorrect:** `result.Value.Should().NotBeNull();`
- **Reference:** See `tests/Eventing.Tests/MemberInvitedEventingTests.cs`, `tests/Eventing.Tests/InMemoryBroker.cs`, `.github/trellis-api-testing-reference.md`.

## Decision Tables

### Modeling decisions

| Scenario | Use | Not |
|---|---|---|
| Expected business failure | `Result<T>` | Exceptions for normal flow |
| Optional value object | `Maybe<T>` | `T?` |
| Domain enum-like concept | `RequiredEnum<T>` | C# `enum` |
| Scalar domain concept | `RequiredString<T>`, `RequiredGuid<T>`, built-in `Trellis.Primitives` | Raw primitives on domain surfaces |
| Cross-service contract field | Primitive (`string`/`Guid`) on an `IIntegrationEvent` | Value object or `Maybe<T>` on the wire |
| Shared domain identity across services | A value object in `SharedKernel` (e.g. `TenantId`) | Re-declaring it per service |

### Validation and authorization decisions

| Scenario | Use | Not |
|---|---|---|
| Static permission gate | `IAuthorize` + `Permissions.*` constant | Handler-side permission `if` |
| Per-resource ownership/tenant check | `IAuthorizeResource<T>` + `IIdentifyResource<T, TId>` + loader | Handler-side ownership checks |
| Shared loader by id | `SharedResourceLoaderById<T, TId>` | Repeating per-command loader code |
| Tenant scoping | `IActorProvider.GetCurrentTenantIdAsync(...)` | A `tenant_id` request parameter |
| Hide existence of a sensitive resource | `AddResourceAuthorization(o => o.HideExistence<T>())` (404 on cross-tenant) | Leaking 403 that confirms existence |
| Required `If-Match` on body-overwriting mutation | `.RequireETag(command.IfMatchETags)` | Omitting it (lost-update race) |

### Handler and endpoint decisions

| Scenario | Use | Not |
|---|---|---|
| Straight-through handler flow | `Ensure`/`Map`/`Bind`/`Tap` (+ `*Async`) | Imperative unwrapping |
| Sync result in a `ValueTask` handler | `Result.Ok(x).AsValueTask()` | `new ValueTask<Result<T>>(...)` / `new(...)` |
| Resource already loaded by the pipeline | `authorized.GetRequiredResource()` | Re-fetching from the repository |
| Map a domain `Result` to HTTP | `result.ToHttpResponse(...)` / `ToHttpResponseAsync(...)` | Manual status-code branching |
| 201 Created with `Location` | `.CreatedAtRoute(name, x => x.Id).WithVersionedRoute()` | Bare `CreatedAtRoute` under versioning |
| Conditional response headers | `.WithETag(...)` / `.WithLastModified(...)` | Hand-writing `ETag`/`Last-Modified` |
| Group-wide conventions | Hoist onto the `MapGroup` once | Repeat on every endpoint |

### EF Core and eventing decisions

| Scenario | Use | Not |
|---|---|---|
| Conventions | `ApplyTrellisConventionsFor<TContext>()` | Manual `HasConversion()`/`OwnsOne()` for Trellis types |
| Interceptors | `AddTrellisInterceptors()` | Reimplement ETag/timestamp plumbing |
| Commit | `AddTrellisUnitOfWork<TContext>()` (pipeline commits) | `SaveChangesAsync()` in a handler |
| Optional lookup | `FirstOrDefaultMaybeAsync(...)` / repository `Maybe<T>` | `FirstOrDefaultAsync(...)` + `null` |
| Produce a cross-service event | Domain event → outbox → translator → `IIntegrationEventCollector` | Synchronous call to the other service |
| Consume a cross-service event | `AddTrellisInbox` + `AddIntegrationEventHandler<,>` + consumer host | Projecting without the inbox (double-apply) |
| Dedup identity | `DeterministicEventId.For…(businessKey)` | A random per-message id |

## Reference Implementation

Study these files before adding a service or feature.

| Pattern | Files |
|---|---|
| Endpoint folder, hoisted group conventions, versioned routes | `Members/Api/src/MemberEndpoints.cs`, `Projects/Api/src/ProjectEndpoints.cs`, `Projects/Api/src/TeamEndpoints.cs` |
| Thin host + per-layer DI extensions | `*/Api/src/Program.cs`, `*/Application/src/DependencyInjection.cs`, `*/Acl/src/DependencyInjection.cs` |
| Permissions in Domain + `IAuthorize` | `*/Domain/src/Permissions.cs`, `Members/Application/src/InviteMemberCommand.cs` |
| Command/query + handler colocated, ROP body | `Members/Application/src/InviteMemberCommand.cs`, `Projects/Application/src/UpdateProjectCommand.cs`, `Projects/Application/src/GetProjectQuery.cs` |
| Server-derived tenant | `*/Application/src/ActorProviderExtensions.cs` |
| Resource authorization + loader | `Projects/Application/src/UpdateProjectCommand.cs`, `Projects/Acl/src/ProjectResourceLoader.cs`, `Members/Acl/src/MemberResourceLoader.cs` |
| Aggregate with value objects + `RequiredEnum` | `Members/Domain/src/Member.cs`, `Members/Domain/src/Role.cs`, `Members/Domain/src/MemberId.cs`, `Projects/Domain/src/Project.cs` |
| Repository (`Maybe<T>`, stages only) | `Members/Application/src/IMemberRepository.cs`, `Members/Acl/src/EfMemberRepository.cs` |
| Outbox producer (domain event → integration event) | `Members/Domain/src/MemberInvited.cs`, `Members/Application/src/MemberInvitedTranslator.cs`, `Members/Acl/src/ServiceBusIntegrationEventPublisher.cs` |
| Published-language contract + deterministic id | `SharedKernel/src/MemberInvitedIntegrationEvent.cs`, `Members/Application/src/DeterministicEventId.cs` |
| Inbox consumer + read model | `Projects/Acl/src/MemberEventsConsumer.cs`, `Projects/Acl/src/MemberInvitedHandler.cs`, `Projects/Application/src/IKnownMemberDirectory.cs` |
| Aspire orchestration, gateway, defaults | `AppHost/src/Program.cs`, `Gateway/src/Program.cs`, `ServiceDefaults/src/Extensions.cs` |
| End-to-end eventing test | `tests/Eventing.Tests/MemberInvitedEventingTests.cs`, `tests/Eventing.Tests/InMemoryBroker.cs` |

## Architecture and Layout

### Service shape

- **Rule:** 🟡 SHOULD structure each service as four projects — `Domain`, `Application`, `Acl`, `Api` — each with `src/` and `tests/`, plus the shared infrastructure projects.
- **Rationale:** The four-layer split keeps the domain pure, the application orchestrating, the Acl adapting persistence/messaging, and the Api presenting — matching the ASP template and the way services deploy independently.

| Layer | Can depend on | Cannot depend on | Contains |
|---|---|---|---|
| Domain | `Trellis.Core`, `Trellis.Primitives`, `SharedKernel` (shared identities); `Trellis.EntityFrameworkCore` analyzer only | EF Core runtime, ASP.NET Core, Mediator | Aggregates, entities, value objects, domain events, **permission constants** |
| Application | Domain, `SharedKernel`, Mediator, `Trellis.Mediator`, `Trellis.Microservices.Abstractions` | ASP.NET Core, EF Core providers | Commands/queries **and their handlers**, repository interfaces, integration-event translators, `IActorProvider` extensions |
| Acl | Application, `SharedKernel`, `Trellis.EntityFrameworkCore`, EF provider, transport SDK | Api types | `DbContext`, repositories, resource loaders, outbox/inbox wiring, integration-event publisher/consumer |
| Api | Domain, Application, Acl, `SharedKernel`, `ServiceDefaults`, `Trellis.Asp` (+ `Trellis.Asp.ApiVersioning`, `Trellis.Authorization`, SLI, `Trellis.Microservices.AspNetCore`) | Domain persistence details | `*Endpoints.cs`, request/response DTOs, `Program.cs`, internal-JWT actor provider |

> **Why "Acl"?** ACL = Anti-Corruption Layer. It adapts external systems (SQL Server, Service Bus, other services) to the domain model.

### Shared and infrastructure projects

| Project | Role |
|---|---|
| `SharedKernel` | Cross-service domain identities (e.g. `TenantId`) **and** published-language integration-event contracts. References only `Trellis.Core` + `Trellis.Primitives` — **not** `Trellis.Authorization` — so keep auth helpers (like the tenant extension) per service. |
| `AppHost` | .NET Aspire orchestration — provisions SQL Server + databases and the Service Bus (emulator in dev), and wires every service and the Gateway. |
| `ServiceDefaults` | `AddServiceDefaults` — OpenTelemetry (incl. SLI instrumentation), health checks, service discovery, HttpClient resilience. References `Trellis.ServiceLevelIndicators` for the SLI OpenTelemetry instrumentation. |
| `Gateway` | YARP reverse proxy + internal-JWT minting (JWKS endpoint) — the only public trust boundary; mints the actor JWT downstream services consume. |

> **Published language vs Shared Kernel.** `TenantId` is a *shared kernel* identity both contexts co-own; an `IIntegrationEvent` is a *published language* contract one context publishes. They live in the same project for the template's sake but evolve under different rules — change a shared identity as a co-owned decision; change a contract as a versioned publish/subscribe decision (add fields, never repurpose; bump `MessageType` on a breaking change).

### Composition-root rules

- Repository **interfaces** live in Application; **implementations** in Acl.
- One `DependencyInjection.cs` per layer: `Add{Service}Application()` (Application) and `Add{Service}Acl(builder)` (Acl).
- `IActorProvider` is registered as the internal-JWT provider in the Api host; register `TimeProvider.System` as a singleton in the Application layer of any service whose handlers use it (Members does; Projects does not).
- The Acl layer **replaces** the default in-process `IIntegrationEventPublisher` with the transport adapter (`services.Replace(ServiceDescriptor.Singleton<IIntegrationEventPublisher, …>())`) so events leave the process.

### Project layout

```text
{Solution}/
├── {Solution}.slnx
├── Directory.Build.props          ← DO NOT MODIFY
├── Directory.Packages.props       ← ADD packages here (versions only); TrellisVersion + TrellisMicroservicesVersion
├── global.json                    ← DO NOT MODIFY
├── .github/
│   ├── copilot-instructions.md    ← THIS FILE
│   └── trellis-api-*.md           ← shipped API reference set
├── AppHost/                       ← Aspire orchestration
├── ServiceDefaults/               ← OpenTelemetry, health, SLI, discovery
├── Gateway/                       ← YARP + internal-JWT minting
├── SharedKernel/                  ← shared identities + integration-event contracts
├── Members/                       ← write-side service
│   ├── Domain/{src,tests}
│   ├── Application/{src,tests}
│   ├── Acl/{src,tests}
│   └── Api/{src,tests}
├── Projects/                      ← read-side consumer service
│   └── … (same four layers)
└── tests/
    └── Eventing.Tests/            ← end-to-end outbox→broker→inbox→read-model
```

> **NuGet packages:** add `<PackageVersion>` to `Directory.Packages.props`, then a `<PackageReference>` without a version in the relevant `.csproj`. Framework pins are the `$(TrellisVersion)` and `$(TrellisMicroservicesVersion)` properties.

## Implementation Order and Build Checkpoints

- **Rule:** 🔴 MUST implement a service Domain → Application → Acl → Api → Tests, running `dotnet build` between layers (source generators emit code — `partial Maybe<T>` backing fields, Mediator wiring — that later layers consume). Adding a **new** service additionally requires registering it (and its database/queue) in `AppHost` and routing it in the `Gateway`.
- **Rationale:** Generated code only appears after compilation, so a later layer cannot reference it until the earlier project has built once; and a new service is not reachable until Aspire provisions it and the gateway routes to it.
- **Correct:**
```text
1. Domain/src      — value objects, aggregates, events, permissions.            → dotnet build
2. Application/src — repository interfaces, commands/queries + handlers,
                     integration-event translators, ActorProvider extension.    → dotnet build
3. Acl/src         — DbContext, repositories, resource loaders, outbox/inbox,
                     integration-event publisher/consumer.                       → dotnet build
4. Api/src         — *Endpoints.cs, DTOs, Program.cs, internal-JWT provider.     → dotnet build
5. AppHost + Gateway — provision DB/queue, add the project, route it.            → dotnet build
6. Tests           — Domain/Application/Acl/Api + Eventing.Tests.               → dotnet test
```
- **Incorrect:** Creating every file across all projects, then a single build after downstream layers already require generated code; or shipping a service the gateway cannot reach.
- **Reference:** See `AppHost/src/Program.cs` and `Gateway/src/Program.cs` for the wiring a new service must join.

> **Verify the round-trip.** Because this template scaffolds via `dotnet new`, a change that builds in place can still break the instantiated output. After modifying template content, build the solution in **Release** (`dotnet build {Solution}.slnx -c Release` — 0 warnings, 0 errors), since CI enforces code style (including `IDE0005` unused-usings) as errors.
