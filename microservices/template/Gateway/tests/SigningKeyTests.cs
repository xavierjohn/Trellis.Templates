using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using ProjectTrackerTemplate.Gateway;

namespace Gateway.Tests;

public sealed class SigningKeyTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "trellis-signing-" + Guid.NewGuid().ToString("N"));

    public SigningKeyTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void Production_requires_persistent_private_key_material()
    {
        var load = () => SigningKeyRegistration.LoadSigningKey(Environment(Environments.Production), Configuration([]));

        load.Should().Throw<InvalidOperationException>().WithMessage("*persistent RSA private-key*");
    }

    [Fact]
    public void Persistent_key_preserves_the_same_public_key_and_kid_across_restarts()
    {
        using var rsa = RSA.Create(2048);
        var path = WriteKey("current.pem", rsa.ExportRSAPrivateKeyPem());
        var settings = Configuration(new() { ["Gateway:SigningKeyPath"] = path });
        var first = SigningKeyRegistration.LoadSigningKey(Environment(Environments.Production), settings);
        var second = SigningKeyRegistration.LoadSigningKey(Environment(Environments.Production), settings);
        using var firstRsa = first.Rsa;
        using var secondRsa = second.Rsa;

        first.KeyId.Should().Be(second.KeyId);
        firstRsa.ExportSubjectPublicKeyInfo().Should().Equal(secondRsa.ExportSubjectPublicKeyInfo());
    }

    [Fact]
    public void Public_only_material_cannot_be_the_active_signer()
    {
        using var rsa = RSA.Create(2048);
        var path = WriteKey("public.pem", rsa.ExportSubjectPublicKeyInfoPem());
        var load = () => SigningKeyRegistration.LoadSigningKey(Environment(Environments.Production),
            Configuration(new() { ["Gateway:SigningKeyPath"] = path }));

        load.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Rotation_can_publish_future_or_retiring_public_keys_without_changing_the_signer()
    {
        using var rsa = RSA.Create(2048);
        using var futureRsa = RSA.Create(2048);
        var settings = Configuration(new()
        {
            ["Gateway:SigningKeyPath"] = WriteKey("current.pem", rsa.ExportRSAPrivateKeyPem()),
            ["Gateway:PublishedKeyPaths:0"] = WriteKey("future.pem", futureRsa.ExportSubjectPublicKeyInfoPem()),
        });
        var environment = Environment(Environments.Production);
        var current = SigningKeyRegistration.LoadSigningKey(environment, settings);
        using var currentRsa = current.Rsa;
        var published = SigningKeyRegistration.LoadPublishedKeys(environment, settings, current);
        using var publishedRsa = published.Single().Rsa;

        published.Single().KeyId.Should().NotBe(current.KeyId);
        publishedRsa.ExportSubjectPublicKeyInfo().Should().Equal(futureRsa.ExportSubjectPublicKeyInfo());
        currentRsa.ExportSubjectPublicKeyInfo().Should().Equal(rsa.ExportSubjectPublicKeyInfo());
    }

    private string WriteKey(string name, string pem)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, pem);
        return path;
    }

    [Fact]
    public void Weak_private_keys_are_rejected()
    {
        using var rsa = RSA.Create(1024);
        var load = () => SigningKeyRegistration.LoadSigningKey(Environment(Environments.Production),
            Configuration(new() { ["Gateway:SigningKeyPath"] = WriteKey("weak.pem", rsa.ExportRSAPrivateKeyPem()) }));

        load.Should().Throw<InvalidOperationException>().WithMessage("*at least 2048 bits*");
    }

    [Fact]
    public void Malformed_key_material_is_rejected()
    {
        var load = () => SigningKeyRegistration.LoadSigningKey(Environment(Environments.Production),
            Configuration(new() { ["Gateway:SigningKeyPath"] = WriteKey("malformed.pem", "not a PEM key") }));

        load.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Published_ring_cannot_repeat_the_active_public_key()
    {
        using var rsa = RSA.Create(2048);
        var settings = Configuration(new()
        {
            ["Gateway:SigningKeyPath"] = WriteKey("current.pem", rsa.ExportRSAPrivateKeyPem()),
            ["Gateway:PublishedKeyPaths:0"] = WriteKey("duplicate.pem", rsa.ExportSubjectPublicKeyInfoPem()),
        });
        var environment = Environment(Environments.Production);
        var current = SigningKeyRegistration.LoadSigningKey(environment, settings);
        using var currentRsa = current.Rsa;
        var load = () => SigningKeyRegistration.LoadPublishedKeys(environment, settings, current);

        load.Should().Throw<InvalidOperationException>().WithMessage("*distinct public keys*");
    }

    private static IHostEnvironment Environment(string name) =>
        new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true, EnvironmentName = name }).Environment;

    private static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}