namespace ProjectTrackerTemplate.Gateway;

using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

internal static class SigningKeyRegistration
{
    internal static RsaSecurityKey LoadSigningKey(IHostEnvironment environment, IConfiguration configuration)
    {
        var path = configuration["Gateway:SigningKeyPath"];
        if (string.IsNullOrWhiteSpace(path) && !environment.IsDevelopment())
            throw new InvalidOperationException("Gateway:SigningKeyPath must identify a persistent RSA private-key PEM file outside Development.");
        return LoadKey(environment, path, requirePrivateKey: true);
    }

    internal static IReadOnlyList<RsaSecurityKey> LoadPublishedKeys(
        IHostEnvironment environment, IConfiguration configuration, RsaSecurityKey signingKey)
    {
        var keys = new List<RsaSecurityKey>();
        var keyIds = new HashSet<string>(StringComparer.Ordinal) { signingKey.KeyId };
        var loaded = false;
        try
        {
            foreach (var entry in configuration.GetSection("Gateway:PublishedKeyPaths").GetChildren())
            {
                if (string.IsNullOrWhiteSpace(entry.Value))
                    throw new InvalidOperationException("Gateway:PublishedKeyPaths must contain non-empty public-key PEM file paths.");
                var key = LoadKey(environment, entry.Value, requirePrivateKey: false);
                keys.Add(key);
                if (!keyIds.Add(key.KeyId))
                    throw new InvalidOperationException("Gateway signing and published keys must have distinct public keys.");
            }

            loaded = true;
            return keys;
        }
        finally
        {
            if (!loaded)
                foreach (var key in keys)
                    key.Rsa.Dispose();
        }
    }

    private static RsaSecurityKey LoadKey(IHostEnvironment environment, string? path, bool requirePrivateKey)
    {
        var rsa = RSA.Create(2048);
        var loaded = false;
        try
        {
            if (!string.IsNullOrWhiteSpace(path))
                rsa.ImportFromPem(File.ReadAllText(Path.GetFullPath(path, environment.ContentRootPath)));
            if (requirePrivateKey)
                _ = rsa.ExportParameters(includePrivateParameters: true);
            if (rsa.KeySize < 2048)
                throw new InvalidOperationException("Gateway keys must be RSA with at least 2048 bits.");

            var key = new RsaSecurityKey(rsa)
            {
                KeyId = Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())),
            };
            loaded = true;
            return key;
        }
        finally
        {
            if (!loaded)
                rsa.Dispose();
        }
    }
}