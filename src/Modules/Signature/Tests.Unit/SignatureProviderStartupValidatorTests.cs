namespace Liakont.Modules.Signature.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.Signature.Infrastructure;
using Liakont.Modules.Signature.Tests.Unit.TestDoubles;
using Xunit;

/// <summary>
/// Couvre l'optionalité de la signature au démarrage (ADR-0027 §4 ; INV-SIGPROV-6) : un tenant
/// <c>Recorded</c> démarre SANS aucun fournisseur (aucune erreur) ; on ne bloque le démarrage QUE pour un
/// fournisseur CONFIGURÉ mais NON câblé. Équivalent de la validation de configuration de l'abstraction IdP,
/// mais — différence essentielle — l'absence n'est jamais une erreur.
/// </summary>
public sealed class SignatureProviderStartupValidatorTests
{
    [Fact]
    public void Validate_NoProviderConfigured_DoesNotThrow_RecordedTenantStartsWithoutProvider()
    {
        // Le cas nominal d'un tenant en acceptation enregistrée : aucun fournisseur, registre vide.
        var registry = new SignatureProviderRegistry([]);

        var act = () => SignatureProviderStartupValidator.Validate([], registry);

        act.Should().NotThrow("la signature est optionnelle — l'absence de fournisseur n'est jamais une erreur");
    }

    [Fact]
    public void Validate_NullConfigured_DoesNotThrow()
    {
        var registry = new SignatureProviderRegistry([]);

        var act = () => SignatureProviderStartupValidator.Validate(null, registry);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_ConfiguredProviderNotWired_Throws_WithFrenchMessage()
    {
        // « Configuré mais malformé » : Signature:EnabledProviders cite un plug-in qu'aucune fabrique n'implémente.
        var registry = new SignatureProviderRegistry([]);

        var act = () => SignatureProviderStartupValidator.Validate(["Yousign"], registry);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Yousign*")
            .WithMessage("*aucun plug-in*");
    }

    [Fact]
    public void Validate_ConfiguredProviderWired_DoesNotThrow()
    {
        var registry = new SignatureProviderRegistry([new FakeSignatureProviderFactory("Yousign")]);

        var act = () => SignatureProviderStartupValidator.Validate(["Yousign"], registry);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_BlankConfiguredEntry_Throws()
    {
        var registry = new SignatureProviderRegistry([new FakeSignatureProviderFactory("Yousign")]);

        var act = () => SignatureProviderStartupValidator.Validate(["  "], registry);

        act.Should().Throw<InvalidOperationException>().WithMessage("*vide*");
    }
}
