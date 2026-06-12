namespace Liakont.Agent.Installer.Tests.Configuration;

using System;
using System.Collections.Generic;
using FluentAssertions;
using Liakont.Agent.Core;
using Liakont.Agent.Installer.Configuration;
using Liakont.Agent.Installer.Profiles;
using Xunit;

/// <summary>
/// Garde de la fusion profil + saisie (F13 §5.2) : un champ affiché prend la saisie, un champ
/// verrouillé/masqué prend la valeur imposée par le profil (la saisie est ignorée), et un champ requis
/// non résolu est une erreur bloquante (jamais d'installation muette).
/// </summary>
public class ConfigurationResolverTests
{
    [Fact]
    public void Champ_affiche_prend_la_saisie()
    {
        ResolvedConfiguration resolved = Resolve(
            Profile((ProfileFieldKeys.OdbcConnection, FieldState.Shown, null)),
            Input((ProfileFieldKeys.OdbcConnection, "DSN=Saisie")));

        resolved.Get(ProfileFieldKeys.OdbcConnection).Should().Be("DSN=Saisie");
    }

    [Fact]
    public void Champ_verrouille_ignore_la_saisie_et_garde_la_valeur_imposee()
    {
        ResolvedConfiguration resolved = Resolve(
            Profile((ProfileFieldKeys.PlatformUrl, FieldState.Locked, "https://impose.fr")),
            Input((ProfileFieldKeys.PlatformUrl, "https://tentative-de-contournement.fr")));

        resolved.Get(ProfileFieldKeys.PlatformUrl).Should().Be("https://impose.fr");
    }

    [Fact]
    public void Champ_masque_prend_la_valeur_du_profil()
    {
        ResolvedConfiguration resolved = Resolve(
            Profile((ProfileFieldKeys.Logging, FieldState.Hidden, "info/90j")),
            InstallationInput.Empty);

        resolved.Get(ProfileFieldKeys.Logging).Should().Be("info/90j");
    }

    [Fact]
    public void Champ_affiche_sans_saisie_retombe_sur_le_defaut_du_profil()
    {
        ResolvedConfiguration resolved = Resolve(
            Profile((ProfileFieldKeys.Schedule, FieldState.Shown, "03:00")),
            InstallationInput.Empty);

        resolved.Get(ProfileFieldKeys.Schedule).Should().Be("03:00");
    }

    [Fact]
    public void Champ_requis_non_resolu_est_une_erreur_bloquante()
    {
        ConfigurationResolver.Resolve(
            Profile(
                (ProfileFieldKeys.PlatformUrl, FieldState.Shown, null),
                (ProfileFieldKeys.ApiKey, FieldState.Shown, null)),
            Input((ProfileFieldKeys.ApiKey, "pk")),
            out IReadOnlyList<string> errors);

        errors.Should().ContainSingle(e => e.IndexOf("platformUrl", StringComparison.Ordinal) >= 0);
    }

    [Fact]
    public void InstanceName_non_precise_retombe_sur_l_instance_par_defaut()
    {
        // Cas mono-instance standard : aucune saisie, aucun défaut de profil → « Default » (cohérent
        // avec l'absence de --instance en ligne de commande). Sans ce repli, l'installation serait
        // refusée (nom d'instance vide).
        ResolvedConfiguration resolved = Resolve(
            Profile((ProfileFieldKeys.InstanceName, FieldState.Shown, null)),
            InstallationInput.Empty);

        resolved.Get(ProfileFieldKeys.InstanceName).Should().Be(AgentInstance.DefaultName);
    }

    [Fact]
    public void InstanceName_impose_par_le_profil_n_est_pas_ecrase_par_le_defaut()
    {
        ResolvedConfiguration resolved = Resolve(
            Profile((ProfileFieldKeys.InstanceName, FieldState.Locked, "ClientA")),
            Input((ProfileFieldKeys.InstanceName, "tentative")));

        resolved.Get(ProfileFieldKeys.InstanceName).Should().Be("ClientA");
    }

    private static ResolvedConfiguration Resolve(IntegratorProfile profile, InstallationInput input)
    {
        return ConfigurationResolver.Resolve(profile, input, out _);
    }

    private static IntegratorProfile Profile(params (string Key, FieldState State, string? Value)[] fields)
    {
        var declarations = new Dictionary<string, FieldDeclaration>(StringComparer.Ordinal);
        foreach ((string Key, FieldState State, string? Value) field in fields)
        {
            declarations[field.Key] = new FieldDeclaration(field.State, field.Value);
        }

        return new IntegratorProfile("test", IntegratorBranding.Empty, declarations);
    }

    private static InstallationInput Input(params (string Key, string? Value)[] values)
    {
        var dict = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach ((string Key, string? Value) pair in values)
        {
            dict[pair.Key] = pair.Value;
        }

        return new InstallationInput(dict);
    }
}
