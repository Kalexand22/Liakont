namespace Liakont.Agent.Installer.Configuration;

using System;

/// <summary>
/// Résultat du test du serveur centralisé (écran serveur, F13 §4.2) : un heartbeat « à blanc » qui
/// diagnostique URL injoignable / clé invalide / clé révoquée / OK. Le diagnostic détaillé est porté
/// par le <see cref="Message"/> français (produit par la sonde réutilisée test-api) ; <see cref="Success"/>
/// distingue le seul cas nominal (plateforme joignable ET clé acceptée).
/// </summary>
internal sealed class PlatformTestResult
{
    public PlatformTestResult(bool success, string message)
    {
        Success = success;
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }

    /// <summary>Vrai si la plateforme est joignable et la clé API acceptée (heartbeat 2xx).</summary>
    public bool Success { get; }

    /// <summary>Message opérateur français (diagnostic : injoignable, clé invalide/révoquée, ou OK).</summary>
    public string Message { get; }
}
