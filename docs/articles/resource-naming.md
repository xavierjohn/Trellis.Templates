# Azure resource naming

When you deploy a Trellis service to Azure at scale — many services, across sovereign clouds and regions —
the **names** of your cloud resources matter. A consistent convention makes resources sortable, groupable in
the portal, and unambiguous across environments; an inconsistent one becomes a tax you pay forever.

This repository defines a **hyperscale-grade resource-naming convention** that the templates adopt.

> **Status: planned.** The convention is documented in full; the seam that bakes it into the templates
> (an `IResourceNamer` abstraction plus an opinionated Azure implementation) is on the roadmap. The
> [capability-parity contract](capability-parity.md) already tracks it as a `planned` capability, so it
> surfaces in the matrix without failing the build yet.

## The shape

Names are **workload-first** so that everything for one microservice groups together when you sort resources
in the Azure portal:

```
{system}-{service}-{type}-{purpose}-{env}-{region}-{stamp}-{instance}
```

- **`{system}-{service}`** — an immutable platform profile that identifies the workload.
- **`{type}`** — the resource type (Key Vault, storage, service bus, …).
- **`{env}`** — the environment, spelled out (the CAF word), shortened only on length overflow.
- **`{region}`** — present for *regional* resources; omitted for cloud-singletons.

## Principles

- **Cloud is an isolation boundary, not a name token.** Sovereign clouds (US, EU, SG, …) are fully isolated
  with their own DNS, so the cloud drives the endpoint suffix and tags — it does not need a hash in the name.
- **Fail, don't truncate.** If a name would exceed a resource type's length limit, the generator fails loudly
  rather than silently truncating into a collision.
- **Stable connect names over physical names.** For paired / geo-DR resources (Service Bus, Event Hubs, SQL
  failover groups), services connect through a stable logical alias, never a region-pinned physical name.
- **Tags carry the rest.** Cloud, environment, and ownership live in **tags** (enforced by Azure Policy), so
  the name stays short and the metadata stays queryable.

## The full specification

The complete convention — the per-resource-type adapter table, the dual-scope storage rules, the geo-DR
lifecycle trap, managed-identity guidance, and the worked examples — lives in
**[`shared/conventions/resource-naming.md`](https://github.com/xavierjohn/Trellis.Templates/blob/main/shared/conventions/resource-naming.md)**.
