using Azure.Monitor.OpenTelemetry.AspNetCore;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using ProjectTrackerTemplate.Members.Api;
using Trellis.Asp.Idempotency;
using Trellis.Asp.Idempotency.Cosmos;
using Trellis.ResourceNaming.Azure;
using Trellis.ServiceLevelIndicators;

namespace Members.Api.Tests;

public class HostingConfigurationTests
{
    private static readonly Action<ILogger, Exception?> LogCompositionTest =
        LoggerMessage.Define(LogLevel.Information, new EventId(1), "Composition test");

    [Fact]
    public void Shared_SLI_configuration_uses_the_configured_deployment_region()
    {
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DeployedEnvironment:Region"] = "use2",
        });
        builder.ConfigureServiceLevelIndicators();

        using var provider = builder.Services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<DeployedEnvironmentOptions>>().Value.Region.Should().Be("use2");
        provider.GetRequiredService<IOptions<ServiceLevelIndicatorOptions>>().Value.LocationId
            .Should().Be(ServiceLevelIndicator.CreateLocationId("public", "use2"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Shared_SLI_configuration_rejects_a_blank_region(string region)
    {
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DeployedEnvironment:Region"] = region,
        });
        var configure = () => builder.ConfigureServiceLevelIndicators();

        configure.Should().Throw<InvalidOperationException>().WithMessage("*DeployedEnvironment:Region*");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Telemetry_registers_only_configured_destinations(bool otlp, bool azureMonitor)
    {
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = otlp ? "http://localhost:4317" : " ",
            ["APPLICATIONINSIGHTS_CONNECTION_STRING"] = azureMonitor
                ? "InstrumentationKey=00000000-0000-0000-0000-000000000001;IngestionEndpoint=https://localhost/"
                : " ",
        });
        builder.ConfigureOpenTelemetry();

        using var provider = builder.Services.BuildServiceProvider();
        provider.GetServices<IConfigureOptions<AzureMonitorOptions>>().Any().Should().Be(azureMonitor);
        if (azureMonitor)
            provider.GetRequiredService<IOptions<AzureMonitorOptions>>().Value.ConnectionString
                .Should().Be(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OTLP_exports_all_three_signals_only_when_a_destination_is_configured(bool enabled)
    {
        var received = new ConcurrentQueue<string>();
        var receiverBuilder = WebApplication.CreateSlimBuilder();
        receiverBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        receiverBuilder.Logging.ClearProviders();
        await using var receiver = receiverBuilder.Build();
        receiver.MapPost("/v1/{signal}", (string signal) =>
        {
            received.Enqueue(signal);
            return Microsoft.AspNetCore.Http.Results.Ok();
        });
        await receiver.StartAsync(TestContext.Current.CancellationToken);

        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = enabled ? receiver.Urls.Single() : null,
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
        });
        builder.ConfigureOpenTelemetry();
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .AddSource("HostingConfigurationTests")
                // Keep the in-process collector out of its own export stream.
                .AddAspNetCoreInstrumentation(options => options.Filter = _ => false))
            .WithMetrics(metrics => metrics.AddMeter("HostingConfigurationTests"));

        using (var provider = builder.Services.BuildServiceProvider())
        {
            provider.GetRequiredService<TracerProvider>();
            provider.GetRequiredService<MeterProvider>();
            provider.GetRequiredService<LoggerProvider>();
            using var source = new ActivitySource("HostingConfigurationTests");
            using (source.StartActivity("composition-test")) { }

            using var meter = new Meter("HostingConfigurationTests");
            meter.CreateCounter<int>("composition-test").Add(1);
            provider.GetRequiredService<MeterProvider>().ForceFlush().Should().BeTrue();
            LogCompositionTest(provider.GetRequiredService<ILoggerFactory>().CreateLogger("HostingConfigurationTests"), null);
            provider.GetRequiredService<LoggerProvider>().ForceFlush().Should().BeTrue();
        }

        if (enabled)
            received.Should().Contain(["traces", "metrics", "logs"]);
        else
            received.Should().BeEmpty();
    }

    [Theory]
    [InlineData("not-an-endpoint", "grpc", "OTEL_EXPORTER_OTLP_ENDPOINT")]
    [InlineData("file:///collector", "grpc", "OTEL_EXPORTER_OTLP_ENDPOINT")]
    [InlineData("http://localhost:4317", "http/json", "OTEL_EXPORTER_OTLP_PROTOCOL")]
    public void Invalid_OTLP_configuration_fails_explicitly(string endpoint, string protocol, string setting)
    {
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = endpoint,
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = protocol,
        });
        var configure = () => builder.ConfigureOpenTelemetry();

        configure.Should().Throw<InvalidOperationException>().WithMessage($"*{setting}*");
    }

    [Fact]
    public void Development_uses_the_in_memory_store_without_Azure_configuration()
    {
        var services = Services();
        services.AddConfiguredIdempotencyStore(Environment(Environments.Development), Configuration([]));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IIdempotencyStore>().Should().BeOfType<InMemoryIdempotencyStore>();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Non_development_rejects_an_in_memory_override(string environment)
    {
        var configure = () => Services().AddConfiguredIdempotencyStore(Environment(environment),
            Configuration(new() { ["Idempotency:Store"] = "InMemory" }));

        configure.Should().Throw<InvalidOperationException>().WithMessage("*only in Development*");
    }

    [Theory]
    [InlineData("Idempotency:Cosmos:Endpoint")]
    [InlineData("Idempotency:Cosmos:DatabaseId")]
    [InlineData("Idempotency:Cosmos:ContainerId")]
    public void Production_rejects_incomplete_Cosmos_configuration(string missing)
    {
        var settings = CosmosSettings();
        settings.Remove(missing);
        var configure = () => Services().AddConfiguredIdempotencyStore(Environment(Environments.Production), Configuration(settings));

        configure.Should().Throw<InvalidOperationException>().WithMessage($"*{missing}*");
    }

    [Theory]
    [InlineData("http://localhost:8081")]
    [InlineData("not-an-endpoint")]
    public void Cosmos_requires_an_absolute_HTTPS_endpoint(string endpoint)
    {
        var settings = CosmosSettings();
        settings["Idempotency:Cosmos:Endpoint"] = endpoint;
        var configure = () => Services().AddConfiguredIdempotencyStore(Environment(Environments.Production), Configuration(settings));

        configure.Should().Throw<InvalidOperationException>().WithMessage("*HTTPS*");
    }

    [Fact]
    public void Production_registers_the_framework_Cosmos_store()
    {
        var services = Services();
        services.AddConfiguredIdempotencyStore(Environment(Environments.Production), Configuration(CosmosSettings()));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IIdempotencyStore>().Should().BeOfType<CosmosIdempotencyStore>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Redis")]
    public void Unknown_store_selections_fail_instead_of_falling_back(string store)
    {
        var configure = () => Services().AddConfiguredIdempotencyStore(Environment(Environments.Development),
            Configuration(new() { ["Idempotency:Store"] = store }));

        configure.Should().Throw<InvalidOperationException>().WithMessage("*Unsupported Idempotency:Store*");
    }

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrellisIdempotency();
        return services;
    }

    private static IHostEnvironment Environment(string name) =>
        new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true, EnvironmentName = name }).Environment;

    private static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    private static Dictionary<string, string?> CosmosSettings() => new()
    {
        ["Idempotency:Cosmos:Endpoint"] = "https://localhost:8081/",
        ["Idempotency:Cosmos:DatabaseId"] = "idempotency",
        ["Idempotency:Cosmos:ContainerId"] = "idempotency",
    };
}