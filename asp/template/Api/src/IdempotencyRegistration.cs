namespace TodoSample.Api;

using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Trellis.Asp.Idempotency;
using Trellis.Asp.Idempotency.Cosmos;

internal static class IdempotencyRegistration
{
    internal static IServiceCollection AddConfiguredIdempotencyStore(
        this IServiceCollection services, IHostEnvironment environment, IConfiguration configuration)
    {
        var store = configuration["Idempotency:Store"] ?? (environment.IsDevelopment() ? "InMemory" : "Cosmos");
        if (string.Equals(store, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            if (!environment.IsDevelopment())
                throw new InvalidOperationException("In-memory idempotency is permitted only in Development. Configure Idempotency:Store=Cosmos.");

            return services.AddInMemoryIdempotencyStore();
        }

        if (!string.Equals(store, "Cosmos", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported Idempotency:Store '{store}'. Supported stores are InMemory (Development only) and Cosmos.");

        var endpoint = configuration["Idempotency:Cosmos:Endpoint"];
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) || endpointUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Idempotency:Cosmos:Endpoint must be an absolute HTTPS endpoint for durable idempotency.");

        var databaseId = configuration["Idempotency:Cosmos:DatabaseId"];
        if (string.IsNullOrWhiteSpace(databaseId))
            throw new InvalidOperationException("Idempotency:Cosmos:DatabaseId is required for durable idempotency.");

        var containerId = configuration["Idempotency:Cosmos:ContainerId"];
        if (string.IsNullOrWhiteSpace(containerId))
            throw new InvalidOperationException("Idempotency:Cosmos:ContainerId is required for durable idempotency.");

        services.AddSingleton(_ => new CosmosClient(endpointUri.AbsoluteUri, new DefaultAzureCredential()));
        return services.AddCosmosIdempotencyStore(databaseId, containerId);
    }
}
