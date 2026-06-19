namespace Liakont.Modules.TenantSettings.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.TenantSettings.Application;
using Liakont.Modules.TenantSettings.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Couvre <see cref="DataProtectionSecretProtector"/> : round-trip par purpose, ISOLATION cryptographique
/// (un texte chiffré sous un purpose ne se déchiffre pas sous un autre), et rétrocompatibilité du chemin
/// sans purpose (= purpose « clé API »). Les purposes distincts protègent client_id / client_secret / clé API
/// d'un compte PA (slice 2).
/// </summary>
public sealed class DataProtectionSecretProtectorTests
{
    [Fact]
    public void Protect_Then_Unprotect_Roundtrips_Under_The_Same_Purpose()
    {
        var protector = Build();
        const string plaintext = "client-secret-FICTIF";

        var ciphertext = protector.Protect(plaintext, PaAccountSecretPurposes.ClientSecret);

        ciphertext.Should().NotBe(plaintext, "le secret est chiffré (texte opaque)");
        protector.Unprotect(ciphertext, PaAccountSecretPurposes.ClientSecret).Should().Be(plaintext);
    }

    [Fact]
    public void Ciphertext_Of_One_Purpose_Does_Not_Decrypt_Under_Another()
    {
        var protector = Build();

        var ciphertext = protector.Protect("the-client-id", PaAccountSecretPurposes.ClientId);

        var act = () => protector.Unprotect(ciphertext, PaAccountSecretPurposes.ClientSecret);

        act.Should().Throw<System.Security.Cryptography.CryptographicException>(
            "l'isolation par purpose empêche de déchiffrer un client_id sous le purpose client_secret");
    }

    [Fact]
    public void Parameterless_Path_Is_The_ApiKey_Purpose()
    {
        var protector = Build();
        const string plaintext = "sk_live_FICTIF";

        // Le chemin historique (sans purpose) == purpose « clé API » : le ciphertext est interchangeable.
        var viaParameterless = protector.Protect(plaintext);
        protector.Unprotect(viaParameterless, PaAccountSecretPurposes.ApiKey).Should().Be(plaintext);

        var viaExplicit = protector.Protect(plaintext, PaAccountSecretPurposes.ApiKey);
        protector.Unprotect(viaExplicit).Should().Be(plaintext);
    }

    private static DataProtectionSecretProtector Build()
    {
        var services = new ServiceCollection();
        services.AddDataProtection();
        var provider = services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
        return new DataProtectionSecretProtector(provider);
    }
}
