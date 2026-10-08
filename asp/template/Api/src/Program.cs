#if (UseApiVersioning)
using Asp.Versioning;
#endif
using Scalar.AspNetCore;
using Trellis.ServiceLevelIndicators;
using TodoSample.AntiCorruptionLayer;
using TodoSample.Api;
using TodoSample.Application;
using Trellis.Asp;
using Trellis.Asp.Idempotency;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
#if (!UsePostgres && !UseSqlServer)
connectionString ??= "Data Source=todos.db";
#endif
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("Configuration 'ConnectionStrings:DefaultConnection' is required for the selected database provider.");

builder.Services
    .AddPresentation(builder.Environment, builder.Configuration)
    .AddApplication()
    .AddAntiCorruptionLayer(connectionString);

var app = builder.Build();

// Create database schema in development (use migrations in production)
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
}

if (app.Environment.IsDevelopment())
{
#if (UseApiVersioning)
    app.MapOpenApi().WithDocumentPerVersion();
    app.MapScalarApiReference(
        options =>
        {
            var descriptions = app.DescribeApiVersions();

            for (var i = 0; i < descriptions.Count; i++)
            {
                var description = descriptions[i];
                var isDefault = i == descriptions.Count - 1;
                options.AddDocument(description.GroupName, description.GroupName, isDefault: isDefault);
            }
        });
#else
    app.MapOpenApi();
    app.MapScalarApiReference();
#endif
}

app.UseTrellisProblemDetails();

app.UseHttpsRedirection();

// Measure every matched request, as early as possible after routing — the SLI middleware emits on the
// way out, so a request a downstream middleware short-circuits before the controller (e.g. an
// idempotency replay) is still counted instead of being silently dropped from the metrics.
app.UseServiceLevelIndicator();

app.UseAuthentication();
app.UseAuthorization();
app.UseTrellisIdempotency();
app.UseScalarValueValidation();
var controllers = app.MapControllers();
if (!app.Environment.IsDevelopment())
    controllers.RequireAuthorization();
// /health is a cross-cutting infra endpoint — it must respond to liveness/readiness probes
// regardless of which API version a client speaks. Tagging it explicitly api-version-neutral
// (rather than relying on it being implicitly outside the MVC versioning pipeline) makes
// `?api-version` truly optional, surfaces it as `Neutral` rather than `Unspecified` in the
// SLI/OpenTelemetry tags, and documents the intent for future readers. We attach the metadata
// directly because `IsApiVersionNeutral()` requires an associated `WithApiVersionSet(...)`,
// which doesn't apply to non-versioned endpoints like health checks.
app.MapHealthChecks("/health")
#if (UseApiVersioning)
    .WithMetadata(new ApiVersionNeutralAttribute())
#endif
    ;

app.Run();

/// <summary>
/// Main entry point for the application.
/// </summary>
public partial class Program
{
}
