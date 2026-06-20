namespace Liakont.Host.Tests.Unit.Startup;

using System;
using System.Collections.Generic;
using FluentAssertions;
using Liakont.Host.Startup;
using Microsoft.Extensions.Configuration;
using Stratum.Common.Infrastructure.Keycloak;
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

    // -----------------------------------------------------------------------
    // Câblage AppBootstrap.ValidateDedicatedRealmConfiguration (RDF11 RL-IDP-8)
    // Ces tests exercent la couche de liaison (lecture de la clé de config +
    // binding KeycloakAdminOptions.IsConfigured) pour garantir qu'une dérive
    // sur le nom de clé ou la structure de binding est détectée immédiatement.
    // -----------------------------------------------------------------------
    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public void ValidateDedicatedRealmConfiguration_FlagAbsentOrFalse_NeverThrows(string? flagValue)
    {
        // Profil SaaS partagé (flag absent ou false) : aucune exigence sur l'API Admin.
        var config = BuildConfig(flagValue, adminConfigured: false);

        var act = () => AppBootstrap.ValidateDedicatedRealmConfiguration(config);

        act.Should().NotThrow(
            "le profil partagé ne requiert pas l'API Admin, quelle que soit sa configuration");
    }

    [Fact]
    public void ValidateDedicatedRealmConfiguration_DedicatedFlag_AdminConfigured_DoesNotThrow()
    {
        // Profil dédié cohérent : l'API Admin est présente → démarrage autorisé.
        var config = BuildConfig("true", adminConfigured: true);

        var act = () => AppBootstrap.ValidateDedicatedRealmConfiguration(config);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateDedicatedRealmConfiguration_DedicatedFlag_AdminNotConfigured_Throws()
    {
        // Incohérence câblée : la clé Keycloak:DedicatedRealmPerTenant=true est lue,
        // IsConfigured est évalué → le démarrage doit être bloqué (fail-closed).
        var config = BuildConfig("true", adminConfigured: false);

        var act = () => AppBootstrap.ValidateDedicatedRealmConfiguration(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DedicatedRealmPerTenant*")
            .WithMessage("*API Admin*");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------
    private static IConfiguration BuildConfig(string? dedicatedRealmFlagValue, bool adminConfigured)
    {
        var entries = new Dictionary<string, string?>();

        if (dedicatedRealmFlagValue is not null)
        {
            entries[$"{KeycloakAdminOptions.SectionName}:DedicatedRealmPerTenant"] = dedicatedRealmFlagValue;
        }

        if (adminConfigured)
        {
            entries[$"{KeycloakAdminOptions.SectionName}:AdminBaseUrl"] = "http://keycloak.exemple.test:8080";
            entries[$"{KeycloakAdminOptions.SectionName}:AdminUsername"] = "admin";
            entries[$"{KeycloakAdminOptions.SectionName}:AdminPassword"] = "s3cr3t-test";
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(entries)
            .Build();
    }
}
