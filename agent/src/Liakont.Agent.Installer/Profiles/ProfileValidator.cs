namespace Liakont.Agent.Installer.Profiles;

using System;
using System.Collections.Generic;
using Liakont.Agent.Core;

/// <summary>
/// Valide le SCHÉMA d'un profil intégrateur bien formé (F13 §5.3). Réutilisé par le packaging
/// multi-profils (OPS08c) : un profil invalide doit faire ÉCHOUER la construction du package — une
/// faute de frappe qui masquerait un champ silencieusement est un faux vert, donc interdite
/// (lessons 2026-06-02, gardes anti-faux-vert).
/// <para>Règles vérifiées :</para>
/// <list type="number">
///   <item>clé inconnue (hors <see cref="ProfileFieldKeys.All"/>) = erreur ;</item>
///   <item>champ « masqué » sans valeur = erreur (sinon donnée implicite cachée) ;</item>
///   <item>secret (apiKey) doté d'une valeur, ou non « affiché », = erreur (jamais imposé, F13 §6) ;</item>
///   <item>champ requis (platformUrl, apiKey) non résolu (ni affiché ni doté d'une valeur) = erreur ;</item>
///   <item>valeur d'« instanceName » invalide comme nom de service Windows = erreur ;</item>
///   <item>valeur imposée d'« odbcConnection » contenant un identifiant (Uid/Pwd/Password) = erreur (F13 §6.1).</item>
/// </list>
/// </summary>
internal static class ProfileValidator
{
    /// <summary>Valide <paramref name="profile"/> et collecte TOUTES les erreurs.</summary>
    public static ProfileValidationResult Validate(IntegratorProfile profile)
    {
        var errors = new List<string>();
        string name = profile.ProfileName;

        foreach (KeyValuePair<string, FieldDeclaration> entry in profile.Fields)
        {
            string key = entry.Key;
            FieldDeclaration declaration = entry.Value;

            if (!ProfileFieldKeys.IsKnown(key))
            {
                errors.Add($"Profil « {name} » : champ inconnu « {key} ». " +
                           "Corrigez la clé ou ajoutez-la au registre des champs profilables.");

                // Un champ inconnu ne porte aucune sémantique connue : on ne vérifie pas ses autres règles.
                continue;
            }

            ValidateMaskedHasValue(name, key, declaration, errors);
            ValidateSecret(name, key, declaration, errors);
            ValidateInstanceName(name, key, declaration, errors);
            ValidateNoEmbeddedOdbcCredentials(name, key, declaration, errors);
        }

        ValidateRequiredResolved(profile, name, errors);

        return ProfileValidationResult.FromErrors(errors);
    }

    private static void ValidateMaskedHasValue(string name, string key, FieldDeclaration declaration, List<string> errors)
    {
        // F13 §5.3 : « masqué » impose une valeur — masquer sans valeur cacherait un défaut implicite.
        if (declaration.State == FieldState.Hidden && !declaration.HasValue)
        {
            errors.Add($"Profil « {name} » : le champ « {key} » est « masqué » sans valeur. " +
                       "Un champ masqué doit fournir une valeur par défaut.");
        }
    }

    private static void ValidateSecret(string name, string key, FieldDeclaration declaration, List<string> errors)
    {
        if (!ProfileFieldKeys.IsSecret(key))
        {
            return;
        }

        // F13 §6 : un secret (apiKey) est saisi au wizard puis chiffré DPAPI — jamais imposé par le profil.
        if (declaration.HasValue)
        {
            errors.Add($"Profil « {name} » : le secret « {key} » ne peut pas recevoir de valeur dans le profil " +
                       "(saisi au wizard puis chiffré DPAPI).");
        }

        if (declaration.State != FieldState.Shown)
        {
            errors.Add($"Profil « {name} » : le secret « {key} » doit rester « affiché » (saisi au wizard) ; " +
                       "il ne peut être ni « verrouillé » ni « masqué ».");
        }
    }

    private static void ValidateInstanceName(string name, string key, FieldDeclaration declaration, List<string> errors)
    {
        // Connaissance de SCHÉMA (pas un if(integrateur)) : « instanceName » entre dans un nom de
        // service Windows. La valeur déclarée, si présente, est validée par AgentInstance.TryParse —
        // réutilisé, jamais redupliqué (CLAUDE.md n°6, F13 §3).
        if (key != ProfileFieldKeys.InstanceName || declaration.DefaultValue == null)
        {
            return;
        }

        if (!AgentInstance.TryParse(declaration.DefaultValue, out _, out string? error))
        {
            errors.Add($"Profil « {name} » : valeur d'« {key} » invalide. {error}");
        }
    }

    private static void ValidateNoEmbeddedOdbcCredentials(string name, string key, FieldDeclaration declaration, List<string> errors)
    {
        // Connaissance de SCHÉMA : « odbcConnection » porte les identifiants de la base source.
        // Une valeur IMPOSÉE par le profil ne doit jamais embarquer d'identifiant dans le .exe versionné
        // (F13 §6.1, CLAUDE.md n°10) — ils sont saisis au wizard puis chiffrés DPAPI. Un DSN nu reste permis.
        if (key != ProfileFieldKeys.OdbcConnection || !declaration.HasValue)
        {
            return;
        }

        string[] credentialTokens = { "pwd=", "password=", "uid=", "user id=" };
        string value = declaration.DefaultValue!;
        foreach (string token in credentialTokens)
        {
            if (value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                errors.Add($"Profil « {name} » : « {key} » impose une valeur contenant un identifiant " +
                           "(Uid/Pwd/Password). Les identifiants ODBC sont saisis au wizard puis chiffrés " +
                           "(F13 §6.1) ; n'imposez qu'un DSN nu.");
                return;
            }
        }
    }

    private static void ValidateRequiredResolved(IntegratorProfile profile, string name, List<string> errors)
    {
        foreach (string key in ProfileFieldKeys.Required)
        {
            // Champ requis non déclaré → « défaut ouvert » → affiché pour saisie → résolu : aucun problème.
            if (!profile.Fields.TryGetValue(key, out FieldDeclaration? declaration))
            {
                continue;
            }

            bool resolvable = declaration.State == FieldState.Shown || declaration.HasValue;
            if (!resolvable)
            {
                errors.Add($"Profil « {name} » : le champ requis « {key} » n'est pas résolu — " +
                           "il n'est ni « affiché » (saisie au wizard) ni doté d'une valeur. " +
                           "L'agent serait non fonctionnel.");
            }
        }
    }
}
