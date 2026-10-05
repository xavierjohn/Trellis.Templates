# Idempotency

Opted-in write endpoints use Trellis's `Idempotency-Key` middleware. Select the middleware with
`AddTrellis(options => options.UseIdempotency())`, then register the store separately.
Vendor stores deliberately have no composition-builder slot.

Development defaults to `AddInMemoryIdempotencyStore`. Outside Development, both templates default
to `AddCosmosIdempotencyStore` and explicitly reject an `Idempotency:Store=InMemory` override.
Unknown stores and incomplete Cosmos settings fail composition; no memory fallback is allowed.

Cosmos requires `Idempotency:Cosmos:Endpoint` (absolute HTTPS), `DatabaseId`, and `ContainerId`.
The client uses `DefaultAzureCredential`; user-assigned identities select their client id through
`AZURE_CLIENT_ID`. Infrastructure, not service startup, creates the database and container.

The shipped Bicep modules match the package contract: partition key `/scope`, `defaultTtl: -1`
for per-item expiry, account-key authentication disabled, and the native Cosmos data contributor
role scoped to the idempotency container. Completed records set their own TTL; reservations must
not be removed by a finite container-wide default. ASP's regional instances share the global store.
The microservices sample wires the store only into Members, which owns the idempotent POST.

Keep SLI middleware before idempotency so cached responses are measured. Keep idempotency after
authentication/authorization so the framework resolves the caller's scope before reserving a key.
The framework owns atomic reservation, response replay, fingerprint comparison and conditional
completion; templates must not reimplement those behaviors.
