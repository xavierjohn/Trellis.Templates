# Ubiquitous Language — Project Tracker

> A **ubiquitous language** (Eric Evans, *Domain-Driven Design*) is the single, rigorous vocabulary the
> team — domain experts and engineers alike — uses **everywhere**: in conversation, in this document, in the
> tests, and verbatim in the code (types, methods, events). When a word changes, the code changes with it,
> and vice versa. A term means exactly one thing **within a bounded context**; the *same word can mean
> different things in different contexts*, and that is a feature, not a bug.
>
> This glossary describes the sample **Project Tracker** domain that ships with the template. Replace it with
> your own domain's language — but keep the discipline: every new, renamed, or retired domain term lands here
> and in the code in the *same* change.

## Bounded contexts

The system is split into separate models, each owning its own language:

| Bounded context | Project | Responsibility | Distinctive policy |
|---|---|---|---|
| **Gateway** | `Gateway/` | Trust boundary: authenticates the caller, mints the internal JWT. | Stamps `tenant_id` + the deny-overrides-allow claim contract. |
| **Projects** | `Projects/` | Operational work items. | Cross-tenant access → **403** (existence is not secret). |
| **Members** | `Members/` | HR-sensitive people records. | Cross-tenant access → **404** via `HideExistence` (existence *is* secret). |

A small **Shared Kernel** (`SharedKernel/`) holds only the vocabulary the contexts must agree on
byte-for-byte (today: `TenantId`). Everything else is **Separate Ways** — each context evolves its own model.

## Cross-cutting terms (Shared Kernel)

- **Tenant** — the isolation boundary. Every aggregate belongs to exactly one tenant; no operation crosses
  tenants. *(code: the `TenantId` carried by `Project` and `Member`.)*
- **TenantId** — the typed identity of a tenant; the platform-wide ABAC principal the gateway stamps and
  every service authorizes against. A value object, so it carries equality, JSON binding, and `IParsable`.
  *(code: `SharedKernel/TenantId.cs` — `RequiredString<TenantId>`, the Shared Kernel.)*

## Identity & access terms

- **Actor** — the authenticated principal making a request, reconstructed on each downstream service from the
  internal JWT. Carries an `Id`, a set of **permissions**, and ABAC **attributes** (including `tenant_id`).
  *(code: `Trellis.Authorization.Actor`, hydrated by `TrellisInternalJwtActorProvider`.)*
- **Internal JWT** — the short-lived token the **Gateway** mints for service-to-service calls inside the
  trust boundary. Carries `tenant_id`, the permission claims, and the deny-overrides-allow contract.
- **Permission** — a capability string an actor must hold to invoke a command or query, e.g. `projects:write`,
  `members:invite`. *(code: `IAuthorize.RequiredPermissions`.)*
- **Deny-overrides-allow** — the authorization invariant: a *forbidden* permission always beats an *allowed*
  one. Enforced via a sentinel + count claim trio, so a proxy that strips the forbidden list is rejected.
- **Cross-tenant access** — a request whose **Actor**'s `tenant_id` does not match the target resource's
  `TenantId`. Always denied — but *how* differs by context (see below).
- **Resource authorization** — the pipeline step that **loads the target resource once**, calls
  `Authorize(actor, resource)` (tenant match + ownership), then hands the *same* instance to the handler — no
  second fetch. *(code: `IAuthorizeResource<T>` + `IIdentifyResource<T,TId>` + `IAuthorizedResource<TMsg,T>`.)*
- **HideExistence** — the policy that collapses a cross-tenant **403** into a **404** for resources whose
  *existence* is itself sensitive, so a caller cannot probe for records in another tenant.
  *(code: `o.HideExistence<Member>()`.)*

## Projects context

- **Project** — a unit of work owned by a principal (its **Owner**), inside a single **Tenant**; has a title
  and a description. *(code: `Projects/Domain/Project.cs`.)*
- **ProjectId** — the service-local identity of a Project (Separate Ways — not shared).
  *(code: `Projects/Domain/ProjectId.cs`.)*
- **Owner** — the principal allowed to edit a Project. Stored as `Project.OwnerId` and compared to `Actor.Id`
  in the update handler — i.e. keyed by the actor's principal id (e.g. `alice`), which is distinct from that
  person's tenant-scoped `MemberId` (e.g. `acme-alice`). *(code: `UpdateProjectCommand.Authorize`.)*
- **Cross-tenant access (Projects)** — returns **403 Forbidden**: the resource exists, but you may not touch it.

## Members context

- **Member** — an HR-sensitive person record inside a single **Tenant**; has an email (PII) and a role.
  *(code: `Members/Domain/Member.cs`.)*
- **MemberId** — the service-local identity of a Member. **Tenant-scoped** (`{tenantId}-{localPart}`) so the
  same email in two tenants never collides. *(code: `Members/Domain/MemberId.cs`.)*
- **Invite** — admitting a new Member to **the actor's own tenant**. The tenant is derived server-side from
  the actor, never read from the request body, so a caller cannot plant a member in another tenant.
  *(code: `InviteMemberCommand` / `InviteMemberHandler`.)*
- **Cross-tenant access (Members)** — returns **404 Not Found** (via `HideExistence`): as far as the caller is
  concerned, the record does not exist.

## One word, two meanings — why contexts matter

> **Cross-tenant access** is the clearest example of a *bounded* term. The *action* is identical — an actor
> reaching for a resource in another tenant — yet **Projects** answers **403** (existence is public) while
> **Members** answers **404** (existence is secret). One phrase, two context-specific meanings; the language
> stays unambiguous *because* it is scoped to a context, not the whole system.

## Keeping it alive

This file is part of the code, not commentary about it. A change that introduces, renames, or retires a
domain term updates this glossary **and** the corresponding types in the same change — that is what makes the
language *ubiquitous* rather than aspirational.
