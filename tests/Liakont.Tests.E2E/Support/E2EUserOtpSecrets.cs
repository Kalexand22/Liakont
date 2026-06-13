namespace Liakont.Tests.E2E.Support;

using System;
using System.Collections.Generic;

/// <summary>
/// Secrets TOTP <b>fictifs de test</b> des utilisateurs du realm E2E (<c>keycloak-e2e-realm.json</c>).
/// Doit rester synchronisé avec le champ <c>secretData.value</c> du credential <c>otp</c> de chaque
/// utilisateur du realm fixture : <see cref="TotpGenerator"/> en dérive le code 2FA pour automatiser
/// le login (RLM01, 2FA imposé).
/// <para>
/// Ce ne sont PAS des secrets de production (CLAUDE.md n°10/18 ne s'applique pas) : ce sont des
/// données de fixture de test, au même titre que les mots de passe <c>Test@1234</c> du realm E2E.
/// </para>
/// </summary>
internal static class E2EUserOtpSecrets
{
    private static readonly Dictionary<string, string> Secrets =
        new(StringComparer.Ordinal)
        {
            ["lecture"] = "LIAKONTE2ELECTURESECRET0000000001",
            ["operateur"] = "LIAKONTE2EOPERATEURSECRET00000002",
            ["parametrage"] = "LIAKONTE2EPARAMETRAGESECRET000003",
            ["superviseur"] = "LIAKONTE2ESUPERVISEURSECRET000004",
            ["tenant2"] = "LIAKONTE2ETENANT2SECRET0000000005",
        };

    /// <summary>Secret OTP fixture de l'utilisateur, ou lève si l'utilisateur n'est pas pré-enrôlé.</summary>
    public static string ForUser(string username)
    {
        ArgumentException.ThrowIfNullOrEmpty(username);
        return Secrets.TryGetValue(username, out var secret)
            ? secret
            : throw new InvalidOperationException(
                $"Aucun secret TOTP E2E connu pour l'utilisateur « {username} ». Le realm E2E impose le 2FA : "
                + "ajoutez son credential otp dans keycloak-e2e-realm.json ET son secret dans E2EUserOtpSecrets.");
    }
}
