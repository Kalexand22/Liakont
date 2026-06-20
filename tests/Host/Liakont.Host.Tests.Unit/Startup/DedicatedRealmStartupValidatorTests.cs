namespace Liakont.Host.Tests.Unit.Startup;

using System;
using FluentAssertions;
using Liakont.Host.Startup;
using Xunit;

/// <summary>
/// Couvre la validation au démarrage du flag de profil de déploiement
/// <c>Keycloak:DedicatedRealmPerTenant</c> (RDF11, redline ADR fondateurs RL-IDP-8) :
/// <list type="bullet">
///   <item>profil SaaS PARTAGÉ (flag = false, défaut) — jamais d'erreur, l'API Admin y est optionnelle ;</item>
///   <item>profil DÉDIÉ (flag = true) — fail-closed : exige l'API Admin Keycloak (pré-requis du
///   provisioning realm-par-tenant), sinon échec explicite au démarrage.</item>
/// </list>
/// La capacité dédiée est latente (hors périmètre INV-0021-* tant qu'elle n'a pas son jeu de preuves) :
/// on bloque plutôt que d'activer une capacité sans son pré-requis (« aucune capacité activée sans preuve »).
/// </summary>
public sealed class DedicatedRealmStartupValidatorTests
{
    [Fact]
    public void Validate_SharedProfile_AdminNotConfigured_DoesNotThrow()
    {
        // Profil SaaS partagé (défaut) : pas de realm par tenant, l'API Admin est optionnelle.
        var act = () => DedicatedRealmStartupValidator.Validate(
            dedicatedRealmPerTenant: false,
            keycloakAdminConfigured: false);

        act.Should().NotThrow(
            "le profil partagé ne provisionne aucun realm par tenant — l'API Admin n'est pas requise");
    }

    [Fact]
    public void Validate_SharedProfile_AdminConfigured_DoesNotThrow()
    {
        var act = () => DedicatedRealmStartupValidator.Validate(
            dedicatedRealmPerTenant: false,
            keycloakAdminConfigured: true);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_DedicatedProfile_AdminConfigured_DoesNotThrow()
    {
        // Profil dédié cohérent : l'API Admin est présente → le realm par tenant est provisionnable.
        var act = () => DedicatedRealmStartupValidator.Validate(
            dedicatedRealmPerTenant: true,
            keycloakAdminConfigured: true);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_DedicatedProfile_AdminNotConfigured_Throws_WithFrenchOperatorMessage()
    {
        // Incohérence : profil dédié activé SANS API Admin → la création de realm par tenant échouerait.
        var act = () => DedicatedRealmStartupValidator.Validate(
            dedicatedRealmPerTenant: true,
            keycloakAdminConfigured: false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DedicatedRealmPerTenant*")
            .WithMessage("*API Admin*");
    }
}
