# ProjectTrackerTemplate

> Generated from [`xavierjohn/Trellis.Microservices.Template`](https://github.com/xavierjohn/Trellis.Microservices.Template).

This is a multi-tenant microservices topology demonstrating the [Trellis framework](https://github.com/xavierjohn/Trellis) and the [Trellis.Microservices](https://github.com/xavierjohn/Trellis.Microservices) packages, scaffolded with `dotnet new trellis-microservices`.

## Quick start

```bash
dotnet run --project AppHost/src
```

That boots the Aspire dashboard at <http://localhost:15151> and brings up three processes:

| Process | Port | Role |
|---|---|---|
| **Gateway** | 5001 | YARP reverse proxy. Mints internal JWTs. Publishes JWKS. |
| **Projects** | dynamic | Operational cluster. Cross-tenant access returns **403**. |
| **Members** | dynamic | HR-sensitive cluster. Cross-tenant access returns **404** (HideExistence). |

Open **`AppHost/src/ProjectTrackerTemplate.http`** in VS Code / Rider / Visual Studio for click-to-send scenarios that exercise every authorization outcome and the cross-service eventing flow (invite a member, then watch them appear in `GET /api/team`).

> **HTTP vs HTTPS.** AppHost's launch profile sets `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true` so the template runs without a dev cert. Switch to HTTPS for production: change `applicationUrl`, drop the flag, and update `Gateway/src/Program.cs` + the downstream `Authority`/`ValidIssuer` URLs to `https://gateway.internal` (or your real prod URL). See <https://aka.ms/aspire/allowunsecuredtransport>.

> **Docker.** Aspire runs SQL Server for **both** services and the Service Bus emulator — **Docker (or Podman) must be running**. Schemas and demo data are created automatically in Development. Projects persists its aggregate, team read model and inbox in its own database.

## Coding-agent API references

Start agents at [`.agentdocs/README.md`](.agentdocs/README.md). `AGENTS.md` and
`.github/copilot-instructions.md` contain managed pointers to this project-aware index.
The pinned `Trellis.AgentDocs` local tool maintains guidance from the approved framework,
microservices, ResourceNaming, and SLI packages.

From this project's Git root, refresh the recorded restore graph after generating the project or
upgrading packages:

```powershell
dotnet tool restore
dotnet restore
dotnet tool run agentdocs sync
dotnet tool run agentdocs check --strict
```

Commit the tool manifest, managed pointers, policy, and `.agentdocs/` with package updates.
AgentDocs is optional; the application builds and runs without it.

## What it demonstrates

### Domain-driven design (DDD)

Two **bounded contexts** with separate models and policies: **Projects** (operational — cross-tenant access is a 403) and **Members** (HR-sensitive — cross-tenant access is a 404 via `HideExistence`). The same phrase, *"cross-tenant access,"* deliberately *means* different things in each — that is what a context boundary is.

`TenantId` lives in a dedicated **`SharedKernel`** project both contexts reference, instead of being copied per service. `tenant_id` is a cross-cutting platform identity — the gateway stamps it into every JWT and every service authorizes against it — so the contexts must agree on one definition byte-for-byte; a duplicated copy could silently drift and break cross-service tenant matching. This is Evans' **Shared Kernel** pattern, kept deliberately minimal: service-local identities (`ProjectId`, `MemberId`) stay in their own service (**Separate Ways**).

The domain vocabulary is catalogued in **[`UBIQUITOUS-LANGUAGE.md`](UBIQUITOUS-LANGUAGE.md)** — the shared glossary that keeps code, tests, docs, and conversation speaking one language.

### Tenant isolation (ABAC)

Every internal JWT carries a `tenant_id` claim. The Trellis actor provider on every downstream service **requires** it (`o.RequiredAttributes = ["tenant_id"]`). A request that somehow reaches a downstream service without `tenant_id` fails at the actor-provider boundary with 401 — never at the handler.

### Resource-based authorization

`UpdateProjectCommand` and `GetProjectQuery` both implement `IAuthorizeResource<Project>` + `IIdentifyResource<Project, ProjectId>`. The `ResourceAuthorizationBehavior` loads the project **once** at the pipeline boundary via `ProjectResourceLoader`, calls `Authorize(actor, project)`, then exposes the same instance to the handler via `IAuthorizedResource<TCommand, Project>`. Handlers do NOT re-fetch.

Falsifiable proof: the `projects.resource_loads` counter (in the Aspire dashboard's Metrics tab) ticks **once** per request. Two ticks per request = the typed load-once accessor pattern has regressed.

### HideExistence pattern (HR-sensitive resources)

Members' API composition calls `.UseResourceAuthorization(policy => policy.HideExistence<Member>())`. That single line collapses cross-tenant 403 into 404 at the response-mapping stage — a caller probing for the existence of an employee in another tenant gets the same 404 they'd get for a non-existent MemberId. Compare with Projects, which intentionally returns 403 on cross-tenant access.

### Persistence (Members) — EF Core + UnitOfWork on SQL Server

The **Members** service is the template's write data plane. `Member` is a Trellis `Aggregate<MemberId>` (so it carries an ETag concurrency token + Created/LastModified timestamps), persisted by `MembersDbContext` over **SQL Server** that Aspire provisions and connection-injects (`AppHost/src/Program.cs`). `EfMemberRepository : RepositoryBase<Member, MemberId>` only *stages* changes; `.UseEntityFrameworkUnitOfWork<MembersDbContext>()` selects the transactional behavior that commits when a command handler succeeds, so handlers never call `SaveChanges`. `ApplyTrellisConventionsFor<MembersDbContext>()` maps the value objects to columns with no hand-written `HasConversion`.

**Projects** uses its own SQL Server context for the Project aggregate, team read model and inbox. Its API root also selects the EF unit of work; the inbox independently commits each received event's projection and dedup record together.

### Cross-service eventing — transactional outbox + inbox over Azure Service Bus

Inviting a member is the template's **asynchronous, cross-context** story: it threads a fact from the Members write model to a Projects read model with no synchronous call between the services.

1. **Raise.** `Member.Invite(...)` raises a `MemberInvited` **domain event** (internal to Members).
2. **Capture (atomic).** The **transactional outbox** (`AddTrellisOutbox()` + the capture interceptor) writes one outbox row per event in the *same* `SaveChanges` as the member — so an event can never be lost in the gap between persisting the member and publishing it.
3. **Translate.** After the commit, the relay re-dispatches the domain event to `MemberInvitedTranslator` (an `IDomainEventHandler<MemberInvited>`) which `Add()`s a `MemberInvitedIntegrationEvent` — the stable, primitive-only **published-language** contract in `SharedKernel` (Evans' *Published Language*, distinct from the Shared Kernel proper). A second handler, `MemberInvitedAuditLogger`, writes the post-commit business-event log.
4. **Publish.** `AddAzureServiceBusIntegrationEventPublisher` selects the shipped `Trellis.Messaging.AzureServiceBus` adapter instead of in-process fan-out. It serializes with Web defaults, takes the topic and Subject from `[IntegrationEventName]`, and preserves the outbox row ID as the broker `MessageId`.
5. **Consume + dedupe (effectively-once).** `AddAzureServiceBusIntegrationEventConsumer` subscribes Projects to that topic. Trellis's consumer owns settlement and dead-lettering and feeds the **transactional inbox** `IInboxDispatcher`. The inbox dedupes on `(ConsumerId, MessageId)` and commits the handler's read-model write **together with** the dedup record in one `SaveChanges`.
6. **Read locally.** `MemberInvitedHandler` upserts a `KnownMember` row; `GET /api/team` answers the tenant's team directory entirely from Projects' **own** store, no call back to Members.

**Transport and business deduplication are different.** A retry of the same outbox row repeats its
`MessageId`, so the inbox skips it. A re-translated event creates a new outbox row with a new ID;
`MemberInvitedHandler` checks `(TenantId, MemberId)` so this still does not insert another member.
There is no deterministic transport-ID helper or payload `EventId`.

AppHost provisions the contract-named topic `projecttracker.members.member-invited.v2` and the
`projects` subscription, with an explicit Subject correlation rule for emulator delivery.
The wire name advances to **v2** because the old payload's `EventId` was removed; existing v1
deployments need an explicit contract/topology migration, not an in-place rename.
Additional independent consumers get their own subscriptions and stable inbox consumer IDs.

Falsifiable proof: invite a member (`POST /api/members`), then `GET /api/team` — the new member appears
without a synchronous call to Members. Both a transport retry and a new row for the same business
invitation leave the directory unchanged.

### Deny-overrides-allow JWT contract

The gateway mints a sentinel + count claim trio (`trellis_actor_contract_version=1`, `trellis_permissions_count`, `trellis_forbidden_permissions_count`) on every internal JWT. The consumer-side `TrellisInternalJwtActorProvider` enforces that contract strictly — a JWT missing either count claim is rejected, defending the deny-overrides-allow invariant against a misbehaving proxy that strips the forbidden-permissions array but leaves the allow list intact.

### Transparent key rotation

The gateway exposes `/.well-known/openid-configuration` + `/.well-known/jwks.json`. Downstream services configure `AddJwtBearer(o.Authority = gatewayUrl)`; ASP.NET Core auto-discovers the signing key and refreshes JWKS on `SecurityTokenSignatureKeyNotFoundException`. **Zero downstream config change required** for key rotation.

For PRODUCTION key rotation (multi-replica gateway, gradual cut-over), see the comment block in `Gateway/src/Program.cs` — there's a 5-step runbook embedded there.

## Project layout

Each microservice is split into the four layers — Domain, Application, Acl, Api — and **every layer is
its own project with `src/` and `tests/` side by side**, the same convention the ASP template uses:

```
Members/
├── Domain/
│   ├── src/    Members.Domain.csproj         — Member aggregate + MemberId + MemberInvited event
│   └── tests/  Members.Domain.Tests.csproj
├── Application/
│   ├── src/    Members.Application.csproj     — Invite/Get handlers + translator + audit logger + IMemberRepository
│   └── tests/  Members.Application.Tests.csproj
├── Acl/
│   ├── src/    Members.Acl.csproj             — EF repo + MembersDbContext (outbox) + Service Bus publisher
│   └── tests/  Members.Acl.Tests.csproj
└── Api/
    ├── src/    Members.Api.csproj             — host: Program.cs + versioned MemberEndpoints
    └── tests/  Members.Api.Tests.csproj
```

`Projects/` follows the same four-layer split: its **Domain** adds the `KnownMember` read model; its
**Application** adds the `ListTeam` query + the `IKnownMemberDirectory` read port; its **Acl** adds the
`ProjectsDbContext` (inbox), the read-model projection handler, and the Service Bus consumer. The
remaining components are single `src/` projects:

```
SharedKernel/     src + tests   — shared kernel (TenantId) + published language (MemberInvited contract)
Gateway/          src           — YARP + JWT minting + JWKS endpoints
ServiceDefaults/  src           — shared OpenTelemetry, health, service discovery
AppHost/          src           — Aspire orchestration (SQL Server + Service Bus emulator) + ProjectTrackerTemplate.http
```

## Testing

Every layer has its own test project (`<Service>/<Layer>/tests`). The leaf layers are unit-tested
(value objects, handlers, the EF mapping over in-memory SQLite). The **Api** layer adds
`WebApplicationFactory` HTTP integration tests that drive the real pipeline — the JWT trust boundary,
versioning, and the resource-authorization outcomes (200 / 403 / 404). A cross-service test under
`tests/Eventing.Tests` boots **both** hosts in one process, joined by an in-memory broker, and proves the
eventing flow end to end: inviting a member surfaces them in the other service's team directory with no
synchronous call between services.

By default the integration tests are **hermetic** — the SQL Server contexts are swapped for in-memory
SQLite, Azure SDK clients are replaced (a no-op publisher in API-only tests / in-memory SDK doubles
that exercise the real Trellis publisher and consumer in eventing tests), and the gateway-minted
JWT is swapped for a test auth scheme. Run everything with:

```
dotnet test --solution ProjectTrackerTemplate.slnx -c Release
```

Set **`USE_REAL_SERVICES=true`** (the default lives in `.runsettings`) to run the *same* Api integration
tests against the real configured SQL Server + Azure Service Bus instead — e.g. a gated CI lane that
validates the production providers.


## Production telemetry and idempotency

The API hosts use `Trellis.ServiceDefaults.AddTrellis` for ASP integration, scalar validation and
the standard ProblemDetails envelope. The local `ServiceDefaults/` project remains the Aspire
telemetry, health and service-discovery layer; these are different components.

Telemetry exporters are opt-in. `OTEL_EXPORTER_OTLP_ENDPOINT` enables traces, metrics and logs
(the default protocol is `grpc`; `http/protobuf` is also supported).
`APPLICATIONINSIGHTS_CONNECTION_STRING` independently enables Azure Monitor. Both may be enabled;
with neither, normal console logging remains and no exporter is registered. Aspire supplies its
dashboard OTLP endpoint in local development.

Members uses in-memory idempotency only in Development. Other environments default to Cosmos and
reject `Idempotency:Store=InMemory`. Configure the following on the **Members service**:

```text
Idempotency__Store=Cosmos
Idempotency__Cosmos__Endpoint=https://<account>.documents.azure.com/
Idempotency__Cosmos__DatabaseId=idempotency
Idempotency__Cosmos__ContainerId=idempotency
```

Use the account's actual endpoint for sovereign clouds. Authentication is through
`DefaultAzureCredential`, not account keys; set `AZURE_CLIENT_ID` for a user-assigned identity.
Provision the database/container before starting the service: `/scope` is the partition key,
`defaultTtl: -1` enables per-item TTL, and the identity needs the native Cosmos data contributor role
scoped to this container. Missing or invalid configuration fails startup instead of falling back
to memory. Adding idempotent endpoints to another service requires its own store composition.

[`infra/production.bicep`](infra/production.bicep) provisions this keyless, serverless Cosmos store,
the Members identity's container-scoped role, and workspace-based Application Insights. Supply
`location`, convention-derived `cosmosAccountName`, `logAnalyticsName`, `applicationInsightsName`,
and the deployed Members identity's `membersPrincipalId`. Its outputs provide the Cosmos endpoint
and Application Insights connection string. This is a dependency stack, not deployment of the
gateway, services, SQL Server or Service Bus; those hosting choices remain application-owned.
Azure resources incur charges.

When using AppHost, its `APPLICATIONINSIGHTS_CONNECTION_STRING` is forwarded to all three hosts.
An AppHost `Idempotency:Cosmos:Endpoint` explicitly selects Cosmos for Members, with optional
`DatabaseId`/`ContainerId` overrides and `AZURE_CLIENT_ID` forwarding; otherwise local development
needs no Cosmos account.
Configure deployed services with `ASPNETCORE_ENVIRONMENT=Production`, their real gateway issuer
and a production authentication/signing-key setup as described below.

## Replacing the dev-mode actor provider

`Gateway/src/Program.cs` registers `AddDevelopmentActorProvider` for the inbound side — it reads an `X-Test-Actor` header so you can curl scenarios without minting real JWTs. **Replace it for production** with one of the actor providers in `Trellis.Asp.Authorization`:

| Provider | Use when |
|---|---|
| `ClaimsActorProvider` | You already have JwtBearer on the gateway and want to project claims into the Actor. |
| `EntraActorProvider` | You're integrating with Microsoft Entra (formerly Azure AD). |
| `NestedJsonPathClaimsActorProvider` | Your IdP nests claims under a deep JSON path. |

See [`xavierjohn/Trellis` cookbook](https://github.com/xavierjohn/Trellis/blob/main/docs/docfx_project/api_reference/trellis-api-cookbook.md) for the production-actor-provider recipes.

## License

MIT.
