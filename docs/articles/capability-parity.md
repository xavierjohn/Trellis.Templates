# Capability parity

Two templates that are supposed to offer "the same" capabilities will drift apart the moment one is improved
and the other is forgotten. A developer who picked the lagging template silently loses a guardrail and never
knows. **Capability parity** is how this repository makes that impossible.

The idea: treat the list of required capabilities as a **single, executable contract**, and run it against
both templates in CI. If a template is missing a required capability, the build goes red.

## The three pieces

### 1. The manifest — the single source of truth

[`shared/capability-parity-manifest.yaml`](https://github.com/xavierjohn/Trellis.Templates/blob/main/shared/capability-parity-manifest.yaml)
lists every capability, which templates must have it, and how to verify it:

```yaml
observability:
  title: OpenTelemetry + exported mediator spans + business-event logging
  requiredFor: [asp, microservices]
  checks:
    - { kind: source-contains, glob: "**/*.cs", pattern: "AddOpenTelemetry" }
    - { kind: source-contains, glob: "**/*.cs", pattern: "AddSource\\(\"Trellis.Mediator\"\\)" }
    - { kind: source-contains, glob: "**/*.cs", pattern: "LoggerMessage" }
```

Checks ask *"is this capability wired anywhere in the template?"* — not *"is the file laid out exactly like
the other template?"* — so each template can satisfy a capability in its own idiomatic way.

### 2. The contract test — the runner

[`shared/contract-tests`](https://github.com/xavierjohn/Trellis.Templates/tree/main/shared/contract-tests) is
a small .NET program that reads the manifest and, for a given template, evaluates every required capability's
checks against generated output. `.trellis-template.json` records the selected options; manifest
`when` conditions select versioning, provider, identity, exporter, and deployment checks. Absence
checks also reject unselected packages and scaffolds. It exits non-zero if a required capability is missing.

```bash
dotnet run --project shared/contract-tests -- \
  shared/capability-parity-manifest.yaml microservices /tmp/generated-microservices
```

A capability marked `status: planned` (for example, [Azure resource naming](resource-naming.md)) is reported
but does not fail the build until it ships.

### 3. CI — the gate

The **Capability parity** workflow runs the contract against both templates on every push and pull request.
Drop `AddServiceLevelIndicator` from a template and the build turns red with exactly which capability
regressed — drift is caught by CI, not by hoping a reviewer notices.

The guidance contract requires architectural rules in the root `AGENTS.md` of each generated project,
including command/handler colocation, Domain permissions, and nullable-field conversion.
`.github/copilot-instructions.md` must delegate to that canonical guide. It also requires the pinned
AgentDocs tool, approval policy, managed index and restore context, Core router, and managed pointers
in both instruction files. AgentDocs owns only the marked pointer blocks, leaving curated rules intact. The
microservices template also requires its four package-owned microservices references. CI checks an
isolated Git-root copy of each template with `agentdocs check --strict --strict-references --content-only`,
so stale guidance, broken instruction pointers, and unresolved cross-package links fail the gate.
The template round trip syncs the selected profile's restore graph and then checks it strictly.
Unversioned output legitimately drops versioning-package guidance from the raw source graph.
Generated AgentDocs entrypoints and solution-item references must follow the selected solution name,
including when the C# namespace is overridden or derived from a name that needs sanitizing.

The guidance gate rejects stale singleton actor/pipeline instructions, the `HttpContext.Items`
actor-cache claim, and marking scoped actor registration as incorrect. These checks apply only
to the generated root `AGENTS.md`, leaving deliberate singleton test fixtures alone.
The ASP-only ordering check rejects advice that puts `AddMediator` before the template's
existing builder call; the guide instead explains the actual presentation-first order
and explicit scoped Mediator registration.

The contract also requires the shipped Trellis composition and ProblemDetails APIs, conditional
OTLP/Azure Monitor exporters, and durable idempotency outside Development. Cosmos infrastructure
must use `/scope`, per-item TTL (`defaultTtl: -1`), keyless authentication and container-scoped native
data-plane access. CI compiles each template's Bicep modules in addition to the source contract.
Runtime composition and loopback-export tests belong to the generated projects.

Both API profiles use the common `Trellis.Asp` `HttpContext.PageUrl` pagination builder.
Versioned hosts must explicitly enable `.UseAsp(asp => asp.UseVersionedPageUrls())`;
unversioned output must omit that policy and optional versioning dependencies.

The source runner does not execute the manifest's declarative `http-status`, `builds`, or
`docs-in-sync` checks; it reports them as skipped. Separate build, generated API regressions, and
AgentDocs gates cover those surfaces. CI generates default, versioned PostgreSQL, Azure SQL Server,
and Azure PostgreSQL profiles, while the packaged regression gate builds and runs each profile.

Stable required checks `contract (asp)` and `contract (microservices)` summarize the whole profile
matrix. Both succeed only when every profile job succeeds; failed, canceled, or skipped profile jobs
fail these gates. Branch protection depends on these stable names, not individual profile names.

Conditional-write parity requires HTTP header parsing, typed command preconditions, and required
ETag checks on overwrite handlers. Both canonical agent guides must distinguish requiring `If-Match`
from honoring it: guarded transitions may admit header-free callers, but must check supplied headers
with `OptionalETag` before mutation. The ASP sample's completion handlers and both API versions are
checked explicitly; integration tests cover success, rejected preconditions, unchanged persisted state
and metadata, and authorization precedence. Microservices currently has no guarded-transition sample,
so its guidance defines the same policy without adding a fictitious operation. The curated guides
record a narrow Recipe 23 erratum until corrected upstream guidance is published and synced.

## Adding or changing a capability

1. Update `shared/capability-parity-manifest.yaml` — add the capability and the checks that prove it.
2. Implement it in **both** templates.
3. Let CI run the contract against both.

> **Never** make the build green by weakening a check to match a template. Fix the template. The contract
> describes what a Trellis service *must* provide; the templates conform to it, not the other way around.
