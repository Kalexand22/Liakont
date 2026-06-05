namespace Liakont.Agent.Cli.Tests;

using System;
using System.Collections.Generic;
using Liakont.Agent.Core.Security;

/// <summary>
/// Doublure de <see cref="ISecretProtector"/> pour les tests du CLI : <c>Protect</c> encadre la valeur,
/// <c>Unprotect</c> la rend telle quelle SAUF si elle a été déclarée « non déchiffrable » (simulation
/// d'un secret non chiffré ou chiffré sur une autre machine). Aucune dépendance à DPAPI.
/// </summary>
internal sealed class FakeSecretProtector : ISecretProtector
{
    private readonly HashSet<string> _undecryptable = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Déclare une valeur protégée comme non déchiffrable (le prochain Unprotect lèvera).</summary>
    public void MarkUndecryptable(string protectedValue) => _undecryptable.Add(protectedValue);

    public string Protect(string plaintext) => "ENC(" + plaintext + ")";

    public string Unprotect(string protectedValue)
    {
        if (_undecryptable.Contains(protectedValue))
        {
            throw new FormatException("valeur non chiffrée sur ce poste (doublure de test).");
        }

        return protectedValue;
    }
}
