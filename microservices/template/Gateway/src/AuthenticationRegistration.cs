#pragma warning disable IDE0047
namespace ProjectTrackerTemplate.Gateway;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Trellis.Asp.Authorization;

internal static class AuthenticationRegistration
{
    internal static IServiceCollection AddGatewayAuthentication(
        this IServiceCollection services, IHostEnvironment environment, IConfiguration configuration)
    {
        services.AddAuthentication();
        services.AddAuthorization();
        if (environment.IsDevelopment())
        {
            services.AddDevelopmentActorProvider(options =>
            {
                options.DefaultActorId = "guest";
                options.DefaultPermissions = new HashSet<string>(StringComparer.Ordinal);
            });
            return services;
        }

#if (UseEntra)
        var tenantId = Required(configuration, "Authentication:TenantId");
        var clientId = Required(configuration, "Authentication:ClientId");
        if (!Guid.TryParse(tenantId, out _) || !Guid.TryParse(clientId, out _))
            throw new InvalidOperationException("Authentication:TenantId and Authentication:ClientId must be GUIDs.");
        var authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
        var audience = clientId;
        const string idClaim = "oid";
        const string tenantClaim = "tid";
        services.AddEntraActorProvider(options =>
        {
            options.IdClaimType = idClaim;
            var originalMapping = options.MapAttributes;
            options.MapAttributes = (claims, context) =>
            {
                var attributes = new Dictionary<string, string>(originalMapping(claims, context), StringComparer.Ordinal)
                {
                    ["tenant_id"] = context.User.FindFirst(tenantClaim)?.Value
                        ?? throw new InvalidOperationException("The authenticated Entra token must contain tid."),
                };
                return attributes;
            };
        });
#else
        var authority = Required(configuration, "Authentication:Authority");
        var audience = Required(configuration, "Authentication:Audience");
        if (!Uri.TryCreate(authority, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Authentication:Authority must be an absolute HTTPS OIDC issuer.");
        const string idClaim = "sub";
        const string tenantClaim = "tenant_id";
        services.AddClaimsActorProvider();
        services.AddCachingActorProvider<GatewayClaimsActorProvider>();
#endif
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.Authority = authority;
            options.Audience = audience;
            options.MapInboundClaims = false;
            options.RequireHttpsMetadata = true;
            options.IncludeErrorDetails = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = context =>
                {
                    if (string.IsNullOrWhiteSpace(context.Principal?.FindFirst(idClaim)?.Value) ||
                        string.IsNullOrWhiteSpace(context.Principal?.FindFirst(tenantClaim)?.Value))
                        context.Fail($"The external token must contain {idClaim} and {tenantClaim}.");
                    return Task.CompletedTask;
                },
            };
        });
        return services;
    }

    private static string Required(IConfiguration configuration, string key) =>
        !string.IsNullOrWhiteSpace(configuration[key])
            ? configuration[key]!
            : throw new InvalidOperationException($"Configuration '{key}' is required outside Development.");
}