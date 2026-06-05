namespace Liakont.Agent.Adapters.EncheresV6.Tests.Fakes;

using System;
using System.Collections.Generic;
using Liakont.Agent.Core.Security;

/// <summary>
/// Doublure de <see cref="ISecretProtector"/> pour les tests de l'adaptateur : <c>Protect</c> encadre la
/// valeur, <c>Unprotect</c> retire l'encadrement (et la rend telle quelle si elle n'est pas encadrée),
/// SAUF si elle a été déclarée « non déchiffrable » (simulation d'un secret chiffré sur une autre
/// machine). Aucune dépendance à DPAPI — les tests restent portables et hors-poste.
/// </summary>
internal sealed class FakeSecretProtector : ISecretProtector
{
    private const string Prefix = "ENC(";
    private const string Suffix = ")";

    private readonly HashSet<string> _undecryptable = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Déclare une valeur protégée comme non déchiffrable (le prochain Unprotect lèvera).</summary>
    public void MarkUndecryptable(string protectedValue) => _undecryptable.Add(protectedValue);

    public string Protect(string plaintext) => Prefix + plaintext + Suffix;

    public string Unprotect(string protectedValue)
    {
        if (_undecryptable.Contains(protectedValue))
        {
            throw new FormatException("valeur non chiffrée sur ce poste (doublure de test).");
        }

        if (protectedValue != null
            && protectedValue.StartsWith(Prefix, StringComparison.Ordinal)
            && protectedValue.EndsWith(Suffix, StringComparison.Ordinal))
        {
            return protectedValue.Substring(Prefix.Length, protectedValue.Length - Prefix.Length - Suffix.Length);
        }

        return protectedValue!;
    }
}
