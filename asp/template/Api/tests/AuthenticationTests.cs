namespace Api.Tests;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TodoSample.Api;

public class AuthenticationTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Non_development_requires_identity_configuration(string environment)
    {
        var register = () => Services().AddConfiguredAuthentication(Environment(environment), Configuration([]));

        register.Should().Throw<InvalidOperationException>().WithMessage("*Authentication:*required*");
    }

    [Fact]
    public async Task Development_does_not_register_a_production_bearer_scheme()
    {
        var services = Services();
        services.AddConfiguredAuthentication(Environment(Environments.Development), Configuration([]));
        using var provider = services.BuildServiceProvider();

        var schemes = await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync();

        schemes.Should().BeEmpty();
    }

    [Fact]
    public void Production_pins_external_bearer_validation_without_exposing_failure_details()
    {
        var services = Services();
        services.AddConfiguredAuthentication(Environment(Environments.Production), Configuration(Settings()));
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        options.MapInboundClaims.Should().BeFalse();
        options.RequireHttpsMetadata.Should().BeTrue();
        options.IncludeErrorDetails.Should().BeFalse();
        options.TokenValidationParameters.ValidateIssuer.Should().BeTrue();
        options.TokenValidationParameters.ValidateAudience.Should().BeTrue();
        options.TokenValidationParameters.ValidateLifetime.Should().BeTrue();
        options.TokenValidationParameters.ValidateIssuerSigningKey.Should().BeTrue();
        options.TokenValidationParameters.RequireSignedTokens.Should().BeTrue();
        options.TokenValidationParameters.RequireExpirationTime.Should().BeTrue();
        options.TokenValidationParameters.ClockSkew.Should().Be(TimeSpan.FromSeconds(30));
    }

#if (UseEntra)
    [Theory]
    [InlineData("Authentication:TenantId")]
    [InlineData("Authentication:ClientId")]
    public void Entra_requires_valid_tenant_and_client_identifiers(string key)
    {
        var settings = Settings();
        settings[key] = "not-a-guid";
        var register = () => Services().AddConfiguredAuthentication(Environment(Environments.Production), Configuration(settings));

        register.Should().Throw<InvalidOperationException>().WithMessage("*GUIDs*");
    }
#else
    [Theory]
    [InlineData("http://identity.example")]
    [InlineData("not-an-issuer")]
    public void JWT_requires_an_absolute_HTTPS_issuer(string authority)
    {
        var settings = Settings();
        settings["Authentication:Authority"] = authority;
        var register = () => Services().AddConfiguredAuthentication(Environment(Environments.Production), Configuration(settings));

        register.Should().Throw<InvalidOperationException>().WithMessage("*HTTPS*");
    }
#endif

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services;
    }

    private static IHostEnvironment Environment(string name) =>
        new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true, EnvironmentName = name }).Environment;

    private static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    private static Dictionary<string, string?> Settings() => new()
    {
        ["Authentication:Authority"] = "https://identity.example",
        ["Authentication:Audience"] = "billing",
        ["Authentication:TenantId"] = "00000000-0000-0000-0000-000000000001",
        ["Authentication:ClientId"] = "00000000-0000-0000-0000-000000000002",
    };
}
