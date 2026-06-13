namespace Liakont.Agent.Installer.Tests.Profiles;

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Liakont.Agent.Installer.Profiles;
using Xunit;

public class IntegratorProfileEngineTests
{
    [Fact]
    public void Shown_field_is_visible_and_editable()
    {
        ResolvedField field = EngineWith((ProfileFieldKeys.OdbcConnection, FieldState.Shown, null))
            .Resolve(ProfileFieldKeys.OdbcConnection);

        field.State.Should().Be(FieldState.Shown);
        field.IsVisible.Should().BeTrue();
        field.IsEditable.Should().BeTrue();
    }

    [Fact]
    public void Locked_field_is_visible_but_not_editable_and_carries_its_value()
    {
        ResolvedField field = EngineWith((ProfileFieldKeys.PlatformUrl, FieldState.Locked, "https://exemple.tld"))
            .Resolve(ProfileFieldKeys.PlatformUrl);

        field.State.Should().Be(FieldState.Locked);
        field.IsVisible.Should().BeTrue();
        field.IsEditable.Should().BeFalse();
        field.DefaultValue.Should().Be("https://exemple.tld");
    }

    [Fact]
    public void Hidden_field_is_not_visible_and_not_editable()
    {
        ResolvedField field = EngineWith((ProfileFieldKeys.Logging, FieldState.Hidden, "info/90j"))
            .Resolve(ProfileFieldKeys.Logging);

        field.State.Should().Be(FieldState.Hidden);
        field.IsVisible.Should().BeFalse();
        field.IsEditable.Should().BeFalse();
        field.DefaultValue.Should().Be("info/90j");
    }

    [Fact]
    public void Undeclared_field_defaults_to_open_shown_editable_without_value()
    {
        // Défaut ouvert (F13 §5.3) : un champ non déclaré est affiché + éditable, sans valeur imposée.
        ResolvedField field = EngineWith().Resolve(ProfileFieldKeys.Schedule);

        field.State.Should().Be(FieldState.Shown);
        field.IsVisible.Should().BeTrue();
        field.IsEditable.Should().BeTrue();
        field.DefaultValue.Should().BeNull();
    }

    [Fact]
    public void ResolveAll_covers_every_known_field_even_when_profile_is_empty()
    {
        IReadOnlyList<ResolvedField> resolved = EngineWith().ResolveAll();

        resolved.Select(f => f.Key).Should().BeEquivalentTo(ProfileFieldKeys.All);
        resolved.Should().OnlyContain(f => f.State == FieldState.Shown, "défaut ouvert pour tout champ non déclaré");
    }

    [Fact]
    public void ResolveAll_includes_declared_keys_outside_the_known_registry()
    {
        // Le moteur itère sur l'union (connus ∪ déclarés) : il est robuste à une clé hors registre
        // (que la validation, elle, rejette). Démontre l'itération data-driven, sans énumération en dur.
        IReadOnlyList<ResolvedField> resolved =
            EngineWith(("champPersonnalise", FieldState.Hidden, "x")).ResolveAll();

        resolved.Select(f => f.Key).Should().Contain("champPersonnalise");
        resolved.Count.Should().Be(ProfileFieldKeys.All.Count + 1);
    }

    private static IntegratorProfileEngine EngineWith(params (string Key, FieldState State, string? Value)[] fields)
    {
        var declarations = new Dictionary<string, FieldDeclaration>(System.StringComparer.Ordinal);
        foreach ((string Key, FieldState State, string? Value) field in fields)
        {
            declarations[field.Key] = new FieldDeclaration(field.State, field.Value);
        }

        var profile = new IntegratorProfile("test", IntegratorBranding.Empty, declarations);
        return new IntegratorProfileEngine(profile);
    }
}
