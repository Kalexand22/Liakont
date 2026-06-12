namespace Liakont.Agent.Installer.Tests.Profiles;

using FluentAssertions;
using Liakont.Agent.Installer.Profiles;
using Xunit;

/// <summary>
/// Tests de la résolution du profil de démarrage (OPS08c) : la logique PURE qui décide, à partir du JSON
/// éventuellement embarqué, d'appliquer le profil intégrateur, le profil par défaut ouvert, ou d'échouer.
/// La lecture native de la ressource (<see cref="EmbeddedProfile"/>) est hors périmètre ici — éprouvée par
/// le round-trip du self-test de packaging (tools/test-installer-packaging.ps1).
/// </summary>
public class StartupProfileResolverTests
{
    [Fact]
    public void No_embedded_profile_falls_back_to_open_default()
    {
        bool ok = StartupProfileResolver.TryResolve(null, out IntegratorProfile profile, out string? error);

        ok.Should().BeTrue();
        error.Should().BeNull();
        profile.Fields.Should().BeEmpty();

        // Défaut ouvert : apiKey (et tout champ non déclaré) est affiché et éditable.
        var engine = new IntegratorProfileEngine(profile);
        engine.ResolveAll().Should().Contain(f => f.Key == ProfileFieldKeys.ApiKey && f.IsEditable);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_embedded_profile_falls_back_to_open_default(string embedded)
    {
        bool ok = StartupProfileResolver.TryResolve(embedded, out IntegratorProfile profile, out string? error);

        ok.Should().BeTrue();
        error.Should().BeNull();
        profile.Fields.Should().BeEmpty();
    }

    [Fact]
    public void Valid_embedded_profile_is_applied()
    {
        const string json = "{ \"profil\": \"int-test\", \"champs\": { " +
            "\"adapter\": { \"etat\": \"masqué\", \"valeur\": \"EncheresV6\" } } }";

        bool ok = StartupProfileResolver.TryResolve(json, out IntegratorProfile profile, out string? error);

        ok.Should().BeTrue();
        error.Should().BeNull();
        profile.ProfileName.Should().Be("int-test");

        var engine = new IntegratorProfileEngine(profile);
        engine.Resolve(ProfileFieldKeys.Adapter).State.Should().Be(FieldState.Hidden);
        engine.Resolve(ProfileFieldKeys.Adapter).DefaultValue.Should().Be("EncheresV6");
    }

    [Fact]
    public void Malformed_embedded_profile_fails_without_silent_fallback()
    {
        bool ok = StartupProfileResolver.TryResolve("{ ceci n'est pas du json", out _, out string? error);

        ok.Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Schema_invalid_embedded_profile_fails_without_silent_fallback()
    {
        // apiKey est un secret : il ne peut être ni doté d'une valeur ni masqué (F13 §6) → schéma invalide.
        const string json = "{ \"profil\": \"int-cassé\", \"champs\": { " +
            "\"apiKey\": { \"etat\": \"masqué\", \"valeur\": \"FUITE\" } } }";

        bool ok = StartupProfileResolver.TryResolve(json, out _, out string? error);

        ok.Should().BeFalse();
        error.Should().Contain("int-cassé");
    }
}
