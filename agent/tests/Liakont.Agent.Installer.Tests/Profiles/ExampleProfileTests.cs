namespace Liakont.Agent.Installer.Tests.Profiles;

using System;
using System.IO;
using FluentAssertions;
using Liakont.Agent.Installer.Profiles;
using Xunit;

/// <summary>
/// Garde de non-régression sur les profils d'EXEMPLE livrés dans <c>config/exemples/</c> : ils doivent
/// rester chargeables et valides (sinon le packaging multi-profils d'OPS08c échouerait sur l'exemple
/// même). Exerce aussi le chargement depuis le disque (et non un JSON inline).
/// </summary>
public class ExampleProfileTests
{
    public static readonly TheoryData<string> ShippedExampleProfiles = new TheoryData<string>
    {
        @"config\exemples\profil-integrateur-exemple.json",
        @"config\exemples\profil-integrateur-hebergeur-exemple.json",
    };

    [Theory]
    [MemberData(nameof(ShippedExampleProfiles))]
    public void Shipped_example_profile_loads_and_is_valid(string relativePath)
    {
        IntegratorProfile profile = IntegratorProfileLoader.Load(ResolveExamplePath(relativePath));

        ProfileValidationResult result = ProfileValidator.Validate(profile);

        result.IsValid.Should().BeTrue(string.Join(" | ", result.Errors));
    }

    [Theory]
    [MemberData(nameof(ShippedExampleProfiles))]
    public void Shipped_example_profile_resolves_every_known_field(string relativePath)
    {
        IntegratorProfile profile = IntegratorProfileLoader.Load(ResolveExamplePath(relativePath));

        var engine = new IntegratorProfileEngine(profile);

        engine.ResolveAll().Should().Contain(f => f.Key == ProfileFieldKeys.ApiKey && f.IsEditable);
    }

    private static string ResolveExamplePath(string relativePath)
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"Profil d'exemple « {relativePath} » introuvable en remontant depuis {AppContext.BaseDirectory}.");
    }
}
