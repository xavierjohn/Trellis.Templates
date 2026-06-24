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
checks against the template's source. It exits non-zero if any required capability is missing.

```bash
dotnet run --project shared/contract-tests -- \
  shared/capability-parity-manifest.yaml microservices microservices/template
```

A capability marked `status: planned` (for example, [Azure resource naming](resource-naming.md)) is reported
but does not fail the build until it ships.

### 3. CI — the gate

The **Capability parity** workflow runs the contract against both templates on every push and pull request.
Drop `AddServiceLevelIndicator` from a template and the build turns red with exactly which capability
regressed — drift is caught by CI, not by hoping a reviewer notices.

## Adding or changing a capability

1. Update `shared/capability-parity-manifest.yaml` — add the capability and the checks that prove it.
2. Implement it in **both** templates.
3. Let CI run the contract against both.

> **Never** make the build green by weakening a check to match a template. Fix the template. The contract
> describes what a Trellis service *must* provide; the templates conform to it, not the other way around.
