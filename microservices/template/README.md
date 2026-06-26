# ProjectTrackerTemplate

> Generated from [`xavierjohn/Trellis.Microservices.Template`](https://github.com/xavierjohn/Trellis.Microservices.Template).

This is a multi-tenant microservices topology demonstrating the [Trellis framework](https://github.com/xavierjohn/Trellis) and the [Trellis.Microservices](https://github.com/xavierjohn/Trellis.Microservices) packages, scaffolded with `dotnet new trellis-microservices`.

## Quick start

```bash
dotnet run --project AppHost
```

That boots the Aspire dashboard at <http://localhost:15151> and brings up three processes:

| Process | Port | Role |
|---|---|---|
| **Gateway** | 5001 | YARP reverse proxy. Mints internal JWTs. Publishes JWKS. |
| **Projects** | dynamic | Operational cluster. Cross-tenant access returns **403**. |
| **Members** | dynamic | HR-sensitive cluster. Cross-tenant access returns **404** (HideExistence). |

Open **`ProjectTrackerTemplate.http`** in VS Code / Rider / Visual Studio for click-to-send scenarios that exercise every authorization outcome.

> **HTTP vs HTTPS.** AppHost's launch profile sets `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true` so the template runs without a dev cert. Switch to HTTPS for production: change `applicationUrl`, drop the flag, and update `Gateway/Program.cs` + the downstream `Authority`/`ValidIssuer` URLs to `https://gateway.internal` (or your real prod URL). See <https://aka.ms/aspire/allowunsecuredtransport>.

> **SQL Server (Docker).** The **Members** service persists to SQL Server, which Aspire runs as a container — **Docker (or Podman) must be running**. The schema is created and seeded automatically on first run (Development only). **Projects** stays in-memory, so the two services deliberately contrast an EF/SQL data plane against an in-memory one.

## What it demonstrates

### Domain-driven design (DDD)

Two **bounded contexts** with separate models and policies: **Projects** (operational — cross-tenant access is a 403) and **Members** (HR-sensitive — cross-tenant access is a 404 via `HideExistence`). The same phrase, *"cross-tenant access,"* deliberately *means* different things in each — that is what a context boundary is.

`TenantId` lives in a dedicated **`SharedKernel`** project both contexts reference, instead of being copied per service. `tenant_id` is a cross-cutting platform identity — the gateway stamps it into every JWT and every service authorizes against it — so the contexts must agree on one definition byte-for-byte; a duplicated copy could silently drift and break cross-service tenant matching. This is Evans' **Shared Kernel** pattern, kept deliberately minimal: service-local identities (`ProjectId`, `MemberId`) stay in their own service (**Separate Ways**).

The domain vocabulary is catalogued in **[`UBIQUITOUS-LANGUAGE.md`](UBIQUITOUS-LANGUAGE.md)** — the shared glossary that keeps code, tests, docs, and conversation speaking one language.

### Tenant isolation (ABAC)

Every internal JWT carries a `tenant_id` claim. The Trellis actor provider on every downstream service **requires** it (`o.RequiredAttributes = ["tenant_id"]`). A request that somehow reaches a downstream service without `tenant_id` fails at the actor-provider boundary with 401 — never at the handler.

### Resource-based authorization

`UpdateProjectCommand` and `GetProjectQuery` both implement `IAuthorizeResource<Project>` + `IIdentifyResource<Project, ProjectId>`. The `ResourceAuthorizationBehavior` loads the project **once** at the pipeline boundary via `ProjectResourceLoader`, calls `Authorize(actor, project)`, then exposes the same instance to the handler via `IAuthorizedResource<TCommand, Project>`. Handlers do NOT re-fetch.

Falsifiable proof: the `projects.resource_loads` counter (in the Aspire dashboard's Metrics tab) ticks **once** per request. Two ticks per request = the v4 accessor pattern has regressed.

### HideExistence pattern (HR-sensitive resources)

`Members/Program.cs` calls `services.AddResourceAuthorization(o => o.HideExistence<Member>())`. That single line collapses cross-tenant 403 into 404 at the response-mapping stage — a caller probing for the existence of an employee in another tenant gets the same 404 they'd get for a non-existent MemberId. Compare with `Projects`, which intentionally returns 403 on cross-tenant access.

### Persistence (Members) — EF Core + UnitOfWork on SQL Server

The **Members** service is the template's *real data plane*. `Member` is a Trellis `Aggregate<MemberId>` (so it carries an ETag concurrency token + Created/LastModified timestamps), persisted by `MembersDbContext` over **SQL Server** that Aspire provisions and connection-injects (`AppHost/Program.cs`). `EfMemberRepository : RepositoryBase<Member, MemberId>` only *stages* changes; the `TransactionalCommandBehavior` registered by `AddTrellisUnitOfWork<MembersDbContext>()` commits the unit of work when a command handler succeeds, so handlers never call `SaveChanges`. `ApplyTrellisConventionsFor<MembersDbContext>()` maps the value objects — the `MemberId` key and the shared-kernel `TenantId` — to columns with no hand-written `HasConversion`.

**Projects** is deliberately left on an in-memory repository — the side-by-side contrast makes the EF/UnitOfWork seam easy to see. (Async cross-service eventing — the outbox/inbox — is the next step.)

### Deny-overrides-allow JWT contract

The gateway mints a sentinel + count claim trio (`trellis_actor_contract_version=1`, `trellis_permissions_count`, `trellis_forbidden_permissions_count`) on every internal JWT. The consumer-side `TrellisInternalJwtActorProvider` enforces that contract strictly — a JWT missing either count claim is rejected, defending the deny-overrides-allow invariant against a misbehaving proxy that strips the forbidden-permissions array but leaves the allow list intact.

### Transparent key rotation

The gateway exposes `/.well-known/openid-configuration` + `/.well-known/jwks.json`. Downstream services configure `AddJwtBearer(o.Authority = gatewayUrl)`; ASP.NET Core auto-discovers the signing key and refreshes JWKS on `SecurityTokenSignatureKeyNotFoundException`. **Zero downstream config change required** for key rotation.

For PRODUCTION key rotation (multi-replica gateway, gradual cut-over), see the comment block in `Gateway/Program.cs` — there's a 5-step runbook embedded there.

## Project layout

```
ProjectTrackerTemplate.slnx
├── Gateway/                 — YARP + JWT minting + JWKS endpoints
├── Projects/                — operational cluster (403 cross-tenant)
│   ├── Domain/              — Project aggregate + ProjectId
│   ├── Application/         — Get/List/Update queries + handlers
│   └── Infrastructure/      — in-memory repository + ProjectResourceLoader
├── Members/                 — HR-sensitive cluster (404 cross-tenant)
│   ├── Domain/              — Member aggregate + MemberId
│   ├── Application/         — Get + Invite queries/commands + handlers
│   └── Infrastructure/      — EF Core repository + DbContext + MemberResourceLoader
├── SharedKernel/            — shared kernel (DDD): TenantId, the cross-cutting tenant identity
├── AppHost/                 — Aspire orchestration (+ SQL Server for Members)
└── ServiceDefaults/         — shared OpenTelemetry, health, service discovery
```

## Replacing the dev-mode actor provider

`Gateway/Program.cs` registers `AddDevelopmentActorProvider` for the inbound side — it reads an `X-Test-Actor` header so you can curl scenarios without minting real JWTs. **Replace it for production** with one of the actor providers in `Trellis.Asp.Authorization`:

| Provider | Use when |
|---|---|
| `ClaimsActorProvider` | You already have JwtBearer on the gateway and want to project claims into the Actor. |
| `EntraActorProvider` | You're integrating with Microsoft Entra (formerly Azure AD). |
| `NestedJsonPathClaimsActorProvider` | Your IdP nests claims under a deep JSON path. |

See [`xavierjohn/Trellis` cookbook](https://github.com/xavierjohn/Trellis/blob/main/docs/docfx_project/api_reference/trellis-api-cookbook.md) for the production-actor-provider recipes.

## License

MIT.
