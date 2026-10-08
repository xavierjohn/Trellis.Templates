namespace TodoSample.Api;

#if (UseApiVersioning)
using Asp.Versioning;
using Asp.Versioning.Conventions;
#endif
#if (UseAzureMonitor)
using Azure.Monitor.OpenTelemetry.AspNetCore;
#endif
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Logs;
#if (UseOtlp)
using OpenTelemetry;
using OpenTelemetry.Exporter;
#endif
using Scalar.AspNetCore;
using Trellis;
using Trellis.ServiceLevelIndicators;
using Trellis.Asp;
#if (UseApiVersioning)
using Trellis.Asp.ApiVersioning;
#endif
using Trellis.Mediator;
using Trellis.ResourceNaming.Azure;
using Trellis.ServiceDefaults;
using TodoSample.Domain;
using TodoSample.Application.Todos;
using TodoSample.AntiCorruptionLayer;

internal static class DependencyInjection
{
    public static IServiceCollection AddPresentation(
        this IServiceCollection services, IHostEnvironment environment, IConfiguration configuration)
    {
        services.ConfigureOpenTelemetry(configuration);
        services.ConfigureServiceLevelIndicators(configuration);
        services.AddControllers();
        services.AddConfiguredAuthentication(environment, configuration);
        services.AddTrellis(options =>
        {
            options
#if (UseApiVersioning)
                .UseAsp(asp => asp.UseVersionedPageUrls())
#else
                .UseAsp()
#endif
                .UseScalarValueValidation()
                .UseProblemDetails()
                .UseIdempotency()
                .UseMediator()
                .UseDomainEvents(typeof(CreateTodoCommand).Assembly)
                .UseFluentValidation(typeof(CreateTodoCommand).Assembly)
                .UseResourceAuthorization(typeof(CompleteTodoCommand).Assembly, typeof(AppDbContext).Assembly)
                .UseEntityFrameworkUnitOfWork<AppDbContext>();
            if (environment.IsDevelopment())
                options.UseDevelopmentActorProvider();
            else
#if (UseEntra)
                options.UseEntraActorProvider(actor => actor.IdClaimType = "oid");
#else
                options.UseClaimsActorProvider();
#endif
        });
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = ctx =>
            {
                // Override Trellis's default 500 wording; preserve the service's support message.
                if (ctx.ProblemDetails.Status == StatusCodes.Status500InternalServerError)
                {
                    ctx.ProblemDetails.Detail =
                        "An error occurred in our API. Please refer the trace id with our support team.";
                }
            };
        });
        services.AddResourceCollectionName<TodoItem>("todos");
        services.AddConfiguredIdempotencyStore(environment, configuration);
#if (UseApiVersioning)
        services.AddApiVersioning(options =>
                options.ApiVersionReader = new QueryStringApiVersionReader())
                .AddMvc(options => options.Conventions.Add(new VersionByNamespaceConvention()))
                .AddApiExplorer()
                .AddOpenApi(options => options.Document.AddScalarTransformers());
#else
        services.AddOpenApi(options => options.AddScalarTransformers());
#endif
        services.AddHealthChecks();

        return services;
    }

    internal static IServiceCollection ConfigureOpenTelemetry(
        this IServiceCollection services, IConfiguration configuration)
    {
        static void configureResource(ResourceBuilder r) => r.AddService(
            serviceName: "TodoSampleService",
            serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown");

        var telemetry = services.AddOpenTelemetry()
            .ConfigureResource(configureResource)
            .WithMetrics(builder =>
            {
                builder.AddAspNetCoreInstrumentation();
                builder.AddServiceLevelIndicatorInstrumentation();
                builder.AddMeter(
                    "Microsoft.AspNetCore.Hosting",
                    "Microsoft.AspNetCore.Server.Kestrel",
                    "System.Net.Http");
            })
            .WithTracing(builder =>
            {
                builder.AddAspNetCoreInstrumentation();
                builder.AddTrellisPrimitivesInstrumentation();
                builder.AddTrellisMediatorInstrumentation();
            });

        services.AddLogging(logging => logging.AddOpenTelemetry(options =>
        {
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;
            // Export structured ILogger state (e.g. LogInformation("User {UserId}", id)) as
            // OTLP log attributes so the Structured Logs view shows the key/value pairs, not
            // just the formatted message.
            options.ParseStateValues = true;
            var resourceBuilder = ResourceBuilder.CreateDefault();
            configureResource(resourceBuilder);
            options.SetResourceBuilder(resourceBuilder);
        }));

#if (UseOtlp)
        var endpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) ||
                (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException("OTEL_EXPORTER_OTLP_ENDPOINT must be an absolute HTTP or HTTPS endpoint.");

            var protocol = configuration["OTEL_EXPORTER_OTLP_PROTOCOL"] switch
            {
                null or "grpc" => OtlpExportProtocol.Grpc,
                "http/protobuf" => OtlpExportProtocol.HttpProtobuf,
                _ => throw new InvalidOperationException("OTEL_EXPORTER_OTLP_PROTOCOL must be grpc or http/protobuf."),
            };
            telemetry.UseOtlpExporter(protocol, endpointUri);
        }
#endif

#if (UseAzureMonitor)
        var connectionString = configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
        if (!string.IsNullOrWhiteSpace(connectionString))
            telemetry.UseAzureMonitor(options => options.ConnectionString = connectionString);
#endif

        return services;
    }

    private static IServiceCollection ConfigureServiceLevelIndicators(
        this IServiceCollection services, IConfiguration configuration)
    {
        // The deployed-environment options are the single source for resource naming and the SLI region.
        var section = configuration.GetSection("DeployedEnvironment");
        services.Configure<DeployedEnvironmentOptions>(section);
        var environment = section.Get<DeployedEnvironmentOptions>() ?? new DeployedEnvironmentOptions();

        // Region is the deployment's telemetry location; fail fast rather than emit a region-less location id.
        var region = environment.Region;
        if (string.IsNullOrWhiteSpace(region))
        {
            throw new InvalidOperationException(
                "Configuration 'DeployedEnvironment:Region' is required for the service-level-indicator location id.");
        }

        var locationId = ServiceLevelIndicator.CreateLocationId("public", region);
        services.AddServiceLevelIndicator(options =>
        {
            options.LocationId = locationId;
        })
        .AddMvc()
#if (UseApiVersioning)
        .AddApiVersion()
#endif
        ;

        return services;
    }
}
