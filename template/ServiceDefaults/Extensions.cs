using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Asp.Versioning;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Trellis.ServiceLevelIndicators;

namespace Microsoft.Extensions.Hosting;

// Aspire-standard ServiceDefaults: shared OTEL + service discovery + health endpoints
// + resilience for every WebApplication in the topology (Gateway, Projects, Members).
//
// Lives in the Microsoft.Extensions.Hosting namespace per Aspire convention so consumers
// just call `builder.AddServiceDefaults()` without an extra using.

public static class Extensions
{
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

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        // Aspire AppHost sets OTEL_EXPORTER_OTLP_ENDPOINT on every referenced project; when
        // run outside Aspire (env var unset), the exporter is a no-op.
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

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
                .WithMetadata(new ApiVersionNeutralAttribute());

            app.MapHealthChecks("/alive", new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live"),
            })
                .WithMetadata(new ApiVersionNeutralAttribute());
        }

        return app;
    }
}
