namespace Liakont.Agent.Installer.Tests.Profiles;

using System;
using System.IO;
using FluentAssertions;
using Liakont.Agent.Installer.Profiles;
using Xunit;

/// <summary>
/// Garde de non-régression sur le profil d'EXEMPLE livré dans <c>config/exemples/</c> : il doit
/// rester chargeable et valide. Exerce aussi le chargement depuis le disque (et non un JSON inline).
/// </summary>
public class ExampleProfileTests
{
    private const string ExampleRelativePath = @"config\exemples\profil-integrateur-exemple.json";

    [Fact]
    public void Shipped_example_profile_loads_and_is_valid()
    {
        IntegratorProfile profile = IntegratorProfileLoader.Load(ExampleProfilePath());

        ProfileValidationResult result = ProfileValidator.Validate(profile);

        result.IsValid.Should().BeTrue(string.Join(" | ", result.Errors));
    }

    [Fact]
    public void Shipped_example_profile_resolves_every_known_field()
    {
        IntegratorProfile profile = IntegratorProfileLoader.Load(ExampleProfilePath());

        var engine = new IntegratorProfileEngine(profile);

        engine.ResolveAll().Should().Contain(f => f.Key == ProfileFieldKeys.ApiKey && f.IsEditable);
    }

    private static string ExampleProfilePath()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, ExampleRelativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"Profil d'exemple « {ExampleRelativePath} » introuvable en remontant depuis {AppContext.BaseDirectory}.");
    }
}
