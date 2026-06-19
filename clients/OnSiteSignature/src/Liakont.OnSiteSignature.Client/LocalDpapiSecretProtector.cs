namespace Liakont.OnSiteSignature.Client;

using System;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Protection LOCALE des secrets du client soft (clé API plateforme, etc.) par DPAPI en portée
/// <see cref="DataProtectionScope.CurrentUser"/> (ADR-0030 §7 ; INV-ONSITE-9). Implémentation locale au
/// client (~30 lignes) : on ne référence JAMAIS le <c>ISecretProtector</c>/<c>DpapiSecretProtector</c> de
/// l'agent (pureté du client, §2). <c>CurrentUser</c> et non <c>LocalMachine</c> : sur un poste de criée
/// PARTAGÉ, une appli interactive mono-session confine le déchiffrement au compte Windows qui a chiffré le
/// secret (moindre privilège). Une entropie applicative fixe cloisonne ces secrets des autres usages DPAPI.
/// </summary>
internal sealed class LocalDpapiSecretProtector
{
    // Entropie applicative (publique, non secrète) : cloisonne les secrets du client sur place vis-à-vis des
    // autres données protégées par DPAPI sur le même poste. Ce n'est PAS une clé — la sécurité vient de DPAPI ;
    // changer cette valeur invaliderait les secrets déjà chiffrés.
    private readonly byte[] _entropy = Encoding.UTF8.GetBytes("Liakont.OnSiteSignature.v1");

    /// <summary>Chiffre une valeur en clair et renvoie sa forme protégée (Base64), liée au compte Windows courant.</summary>
    /// <param name="plaintext">Valeur en clair à protéger.</param>
    /// <returns>La valeur protégée encodée en Base64.</returns>
    public string Protect(string plaintext)
    {
        if (plaintext is null)
        {
            throw new ArgumentNullException(nameof(plaintext));
        }

        byte[] clear = Encoding.UTF8.GetBytes(plaintext);
        byte[] encrypted = ProtectedData.Protect(clear, _entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    /// <summary>Déchiffre une valeur protégée (utilisée en mémoire uniquement).</summary>
    /// <param name="protectedValue">Valeur protégée (Base64) produite par <see cref="Protect"/>.</param>
    /// <returns>La valeur en clair.</returns>
    public string Unprotect(string protectedValue)
    {
        if (protectedValue is null)
        {
            throw new ArgumentNullException(nameof(protectedValue));
        }

        byte[] encrypted = Convert.FromBase64String(protectedValue);
        byte[] clear = ProtectedData.Unprotect(encrypted, _entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(clear);
    }
}
