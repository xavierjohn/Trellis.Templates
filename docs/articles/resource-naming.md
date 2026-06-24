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
{system}-{service}-{type}-{env}-{region}-{stamp}-{instance}
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
- **Tags carry the rest.** Cloud, environment, ownership, and a resource's **role/purpose** live in **tags**
  (enforced by Azure Policy), so the name stays short and the metadata stays queryable.
- **One disambiguator, not two.** When a slice has more than one resource of the same type, they're told
  apart by an `{instance}` ordinal — not by a role token in the name. This keeps a single rule across every
  resource type, including ones with tiny name budgets like Storage (24 chars), where a role token often
  won't fit at all.

## Examples

Using the template's own domain — system `ptk` (ProjectTracker), service `mbr` (Members), `prod`, EU cloud /
West Europe (`weu`). Notice where a resource's **role lives in a tag** rather than in the name.

### Cloud-singleton (one per cloud — no region token)

| Resource | Name | Key tags |
|---|---|---|
| Resource group (system slice) | `rg-ptk-prod` | `system=ptk env=prod` |
| Service Bus namespace (alias) | `ptk-sbns-prod` | `cloud=eu` |
| SQL logical server | `ptk-sql-prod` | |
| SQL database | `ptk-mbr-sqldb-prod` | |
| Storage — shared blob | `ptkmbrstprod` | `purpose=blob` |
| Container Registry | `ptkcrprod` | |
| Log Analytics | `ptk-log-prod` | |

### Regional (region token included)

| Resource | Name | Key tags |
|---|---|---|
| Resource group (service slice) | `rg-ptk-mbr-prod-weu-001` | `region=weu` |
| Key Vault | `ptk-mbr-kv-prod-weu` | |
| App Service | `ptk-mbr-app-prod-weu` | |
| Managed identity (app) | `ptk-mbr-id-prod-weu-001` | `purpose=app` |
| Managed identity (deploy) | `ptk-mbr-id-prod-weu-002` | `purpose=deploy` |

### Same type, several in one slice → `{instance}` + role tag

| Role | Name | Key tags |
|---|---|---|
| Shared blob store | `ptkmbrstprod` | `purpose=blob` |
| EH-checkpoint store — West Europe | `ptkmbrstprodweu` | `purpose=ehcheckpoint region=weu` |
| EH-checkpoint store — North Europe | `ptkmbrstprodneu` | `purpose=ehcheckpoint region=neu` |
| Two same-scope blob stores | `ptkmbrstprod01` · `ptkmbrstprod02` | `purpose=blob` (each) |

The blob and checkpoint stores never collide — they differ by scope/region. Only genuine same-scope
duplicates need the `01`/`02` ordinal.

### Geo-DR paired Service Bus (services use the alias)

| Role | Name |
|---|---|
| Alias (failover-stable; services read/write this) | `ptk-sbns-prod` |
| Primary namespace | `ptk-sbns-prod-weu` |
| Secondary namespace | `ptk-sbns-prod-neu` |

### Scale-unit (`{stamp}`) — a whole second copy of the slice

```
rg-ptk-mbr-prod-weu-001       cell 001
rg-ptk-mbr-prod-weu-002       cell 002 — tenants / capacity partitioned across stamps
ptk-mbr-kv-prod-weu-001  vs   ptk-mbr-kv-prod-weu-002      per-stamp resources
```

### Isolated vs Shared (commercial Azure adds a `{u5}` uniqueness hash)

```
Isolated (sovereign):    ptk-sbns-prod        ptkmbrstprod          ptk-mbr-kv-prod-weu
Shared (commercial):     ptk-sbns-prod-k7x2q  ptkmbrstprodk7x2q     ptk-mbr-kv-prod-weu-k7x2q
Shared + storage tight:  ptkmbrstpweuk7x2q    (env shortened to `p` to fit 24 chars)
```

## The full specification

The complete convention — the per-resource-type adapter table, the dual-scope storage rules, the geo-DR
lifecycle trap, managed-identity guidance, and the worked examples — lives in
**[`shared/conventions/resource-naming.md`](https://github.com/xavierjohn/Trellis.Templates/blob/main/shared/conventions/resource-naming.md)**.
