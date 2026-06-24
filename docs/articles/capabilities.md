# Capabilities

Every Trellis template ships the same set of **cross-cutting capabilities**. You don't wire them up — they're
already there, consistently, in both templates. This page explains what each one does and how you use it.

> These capabilities are not aspirational. Each is asserted by the [capability-parity contract](capability-parity.md),
> so a template that drops one fails CI.

## API versioning

APIs are versioned by **date** (e.g. `2026-03-26`). A new version is a new, additive thing — you never
silently break an existing client. In the ASP template each version is its own controller namespace; in the
microservices template each version is an endpoint group. Clients select a version per the configured
version reader, and each version gets its own OpenAPI document.

## Service Level Indicators

Every operation emits an **`operation.duration`** metric tagged with the operation name, derived
automatically from the route (e.g. `GET /api/members/{id}`). That gives you per-endpoint latency and
availability with no manual instrumentation, ready to drive SLOs and dashboards.

## Idempotency

Mutating endpoints honor an **`Idempotency-Key`** header: a retried request with the same key returns the
original result instead of performing the work twice. Network blips and client retries stop being a
data-integrity problem.

## Problem Details

Every error is an **RFC 9457 `application/problem+json`** document — a stable, machine-readable shape with a
`type`, `title`, `status`, and `detail`. Validation and business-rule failures return **`422 Unprocessable
Content`** (per the RFC, `400` is reserved for syntactically malformed requests), so clients can react to
errors programmatically.

## OpenAPI & Scalar

Each API version publishes an **OpenAPI document** at `/openapi/{version}.json`, and a modern
**[Scalar](https://scalar.com/)** API reference UI to explore and try the API in the browser. The OpenAPI
document is generated from your code, so it never drifts from reality.

## Observability

Services are instrumented with **OpenTelemetry** for traces, metrics, and logs. Crucially, the mediator
pipeline's spans are exported and **business events are logged structurally**, all sharing the same trace
and span ids — so a single request can be followed from the HTTP edge, across services, through each handler,
in one trace.

## Authorization

Authorization is **actor-based and declared on the message**. A command or query states the permissions it
requires (and, for resource-based checks, how to load the resource), and the mediator pipeline enforces it
before the handler runs. Authorization logic lives in one place, not scattered through handlers.

## Scalar value validation

Domain ids and other scalars are **value objects**, not raw `Guid`/`string`. If a caller puts a malformed id
in the route, the binding fails cleanly into a **`422`** ProblemDetails — never an unhandled `500`. Validity
is enforced at the type level, at the edge.

## Mediator pipeline

Commands and queries flow through a **mediator pipeline** of behaviors — validation, authorization, tracing,
and more — so handlers stay focused on domain logic. Cross-cutting concerns are pipeline steps, added once.

## Health checks

Services expose **health endpoints** (`/health` and friends) for liveness and readiness, ready for
container orchestrators and load balancers to probe.
