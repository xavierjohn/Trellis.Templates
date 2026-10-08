#pragma warning disable IDE0047
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
#if (UseApiVersioning)
using Asp.Versioning;
#endif
#if (!NoAzureMonitor)
using Azure.Monitor.OpenTelemetry.AspNetCore;
#endif
#if (!NoOtlp)
using OpenTelemetry;
using OpenTelemetry.Exporter;
#endif
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Trellis.ServiceLevelIndicators;
using Trellis.ResourceNaming.Azure;

namespace Microsoft.Extensions.Hosting;

// Aspire-standard ServiceDefaults: shared OTEL + service discovery + health endpoints
// + resilience for every WebApplication in the topology (Gateway, Projects, Members).
//
// Lives in the Microsoft.Extensions.Hosting namespace per Aspire convention so consumers
// just call `builder.AddServiceDefaults()` without an extra using.

public static class Extensions
{
    public static string GetGatewayIssuer(this IHostApplicationBuilder builder)
    {
        var issuer = builder.Configuration["Gateway:Issuer"];
        if (string.IsNullOrWhiteSpace(issuer))
        {
            if (!builder.Environment.IsDevelopment())
                throw new InvalidOperationException("Gateway:Issuer is required outside Development and must match the downstream issuer configuration.");
            issuer = "TEMPLATE_GATEWAY_ISSUER_URL";
        }

        if (!Uri.TryCreate(issuer, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(builder.Environment.IsDevelopment() && uri.Scheme == Uri.UriSchemeHttp)))
            throw new InvalidOperationException("Gateway:Issuer must be an absolute HTTPS URL (HTTP is Development-only).");
        return issuer;
    }

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Resilience: retries + circuit breaker + timeout (Aspire default policy).
            http.AddStandardResilienceHandler();

            // Wire service-discovery name resolution into every HttpClient.
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
            logging.ParseStateValues = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddServiceLevelIndicatorInstrumentation()
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation())
            .WithTracing(tracing => tracing
                .AddSource(builder.Environment.ApplicationName)
                // The Trellis mediator emits one span per command/query under the "Trellis.Mediator"
                // ActivitySource — the per-operation surface for following a request into a service
                // and localising failures. Without this, those spans are created but never exported,
                // so a distributed trace would show only the HTTP hops, not the operation inside.
                .AddSource("Trellis.Mediator")
                // The gateway's YARP reverse-proxy hop (a no-op in services that don't proxy).
                .AddSource("Yarp.ReverseProxy")
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation());

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    public static TBuilder ConfigureServiceLevelIndicators<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var section = builder.Configuration.GetSection("DeployedEnvironment");
        builder.Services.Configure<DeployedEnvironmentOptions>(section);
        var environment = section.Get<DeployedEnvironmentOptions>() ?? new DeployedEnvironmentOptions();
        var region = environment.Region;
        if (string.IsNullOrWhiteSpace(region))
            throw new InvalidOperationException(
                "Configuration 'DeployedEnvironment:Region' is required for the service-level-indicator location id.");

        var locationId = ServiceLevelIndicator.CreateLocationId("public", region);
        builder.Services.AddServiceLevelIndicator(options => options.LocationId = locationId)
            .Enrich(context =>
            {
                var tenantId = context.HttpContext.User.FindFirst("tenant_id")?.Value;
                if (!string.IsNullOrEmpty(tenantId))
                    context.SetCustomerResourceId($"tenant://{tenantId}");
            })
#if (UseApiVersioning)
            .AddApiVersion()
#endif
            ;
        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
#if (!NoOtlp)
        var endpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) ||
                (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException("OTEL_EXPORTER_OTLP_ENDPOINT must be an absolute HTTP or HTTPS endpoint.");

            var protocol = builder.Configuration["OTEL_EXPORTER_OTLP_PROTOCOL"] switch
            {
                null or "grpc" => OtlpExportProtocol.Grpc,
                "http/protobuf" => OtlpExportProtocol.HttpProtobuf,
                _ => throw new InvalidOperationException("OTEL_EXPORTER_OTLP_PROTOCOL must be grpc or http/protobuf."),
            };
            builder.Services.AddOpenTelemetry().UseOtlpExporter(protocol, endpointUri);
        }

#endif
#if (!NoAzureMonitor)
        var connectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
        if (!string.IsNullOrWhiteSpace(connectionString))
            builder.Services.AddOpenTelemetry().UseAzureMonitor(options => options.ConnectionString = connectionString);

#endif
        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Health endpoints are intentionally only exposed in Development to avoid
        // shipping a free attack-surface enumeration in production builds.
        if (app.Environment.IsDevelopment())
        {
            // Tag the health endpoints API-version-neutral so they answer without ?api-version and
            // surface as "Neutral" (not "Unspecified") in the SLI / OpenTelemetry version dimension.
            app.MapHealthChecks("/health")
#if (UseApiVersioning)
                .WithMetadata(new ApiVersionNeutralAttribute());
#else
                ;
#endif

            app.MapHealthChecks("/alive", new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live"),
            })
#if (UseApiVersioning)
                .WithMetadata(new ApiVersionNeutralAttribute());
#else
                ;
#endif
        }

        return app;
    }
}