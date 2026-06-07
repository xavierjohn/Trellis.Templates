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

## What it demonstrates

### Tenant isolation (ABAC)

Every internal JWT carries a `tenant_id` claim. The Trellis actor provider on every downstream service **requires** it (`o.RequiredAttributes = ["tenant_id"]`). A request that somehow reaches a downstream service without `tenant_id` fails at the actor-provider boundary with 401 — never at the handler.

### Resource-based authorization

`UpdateProjectCommand` and `GetProjectQuery` both implement `IAuthorizeResource<Project>` + `IIdentifyResource<Project, ProjectId>`. The `ResourceAuthorizationBehavior` loads the project **once** at the pipeline boundary via `ProjectResourceLoader`, calls `Authorize(actor, project)`, then exposes the same instance to the handler via `IAuthorizedResource<TCommand, Project>`. Handlers do NOT re-fetch.

Falsifiable proof: the `projects.resource_loads` counter (in the Aspire dashboard's Metrics tab) ticks **once** per request. Two ticks per request = the v4 accessor pattern has regressed.

### HideExistence pattern (HR-sensitive resources)

`Members/Program.cs` calls `services.AddResourceAuthorization(o => o.HideExistence<Member>())`. That single line collapses cross-tenant 403 into 404 at the response-mapping stage — a caller probing for the existence of an employee in another tenant gets the same 404 they'd get for a non-existent MemberId. Compare with `Projects`, which intentionally returns 403 on cross-tenant access.

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
│   └── Infrastructure/      — in-memory repository + MemberResourceLoader
├── AppHost/                 — Aspire orchestration
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
