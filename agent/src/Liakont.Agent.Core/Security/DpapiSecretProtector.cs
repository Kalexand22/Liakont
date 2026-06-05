namespace Liakont.Agent.Core.Security;

using System;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Protection des secrets par DPAPI en portée MACHINE (<see cref="DataProtectionScope.LocalMachine"/>) :
/// le service (LocalSystem) et le CLI (intégrateur) tournent sous des comptes différents sur le même
/// poste — la portée machine permet à l'un de déchiffrer ce que l'autre a chiffré (F12 §2.4).
/// Une entropie applicative fixe cloisonne ces secrets vis-à-vis d'autres usages DPAPI de la machine.
/// </summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    // Entropie applicative (publique, non secrète) : cloisonne les secrets Liakont des autres
    // données protégées par DPAPI sur la même machine. Ce n'est PAS une clé — la sécurité vient
    // de DPAPI ; changer cette valeur invaliderait les secrets déjà chiffrés.
    private static readonly byte[] _entropy = Encoding.UTF8.GetBytes("Liakont.Agent.v1");

    /// <inheritdoc />
    public string Protect(string plaintext)
    {
        if (plaintext is null)
        {
            throw new ArgumentNullException(nameof(plaintext));
        }

        byte[] clear = Encoding.UTF8.GetBytes(plaintext);
        byte[] encrypted = ProtectedData.Protect(clear, _entropy, DataProtectionScope.LocalMachine);
        return Convert.ToBase64String(encrypted);
    }

    /// <inheritdoc />
    public string Unprotect(string protectedValue)
    {
        if (protectedValue is null)
        {
            throw new ArgumentNullException(nameof(protectedValue));
        }

        byte[] encrypted = Convert.FromBase64String(protectedValue);
        byte[] clear = ProtectedData.Unprotect(encrypted, _entropy, DataProtectionScope.LocalMachine);
        return Encoding.UTF8.GetString(clear);
    }
}
