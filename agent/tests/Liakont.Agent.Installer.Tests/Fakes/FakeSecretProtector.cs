namespace Liakont.Agent.Installer.Tests.Fakes;

using System;
using System.Text;
using Liakont.Agent.Core.Security;

/// <summary>
/// Doublure déterministe de <see cref="ISecretProtector"/> pour les tests (« Core mocké »). Elle TRANSFORME
/// réellement le secret (base64 dans un marqueur) au lieu d'appeler DPAPI : le texte en clair n'est donc
/// PAS un sous-texte de la valeur protégée, ce qui permet d'asserter qu'un secret a bien été chiffré ET
/// qu'il n'apparaît jamais en clair dans agent.json.
/// </summary>
internal sealed class FakeSecretProtector : ISecretProtector
{
    private const string Prefix = "ENC(";
    private const string Suffix = ")";

    public string Protect(string plaintext)
    {
        if (plaintext == null)
        {
            throw new ArgumentNullException(nameof(plaintext));
        }

        return Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext)) + Suffix;
    }

    public string Unprotect(string protectedValue)
    {
        if (protectedValue != null && protectedValue.StartsWith(Prefix, StringComparison.Ordinal) && protectedValue.EndsWith(Suffix, StringComparison.Ordinal))
        {
            string body = protectedValue.Substring(Prefix.Length, protectedValue.Length - Prefix.Length - Suffix.Length);
            return Encoding.UTF8.GetString(Convert.FromBase64String(body));
        }

        throw new FormatException("Valeur non chiffrée par la doublure.");
    }
}
