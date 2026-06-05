namespace Liakont.Agent.Cli.Diagnostics;

using System;

/// <summary>
/// Résultat d'une sonde de la plateforme (F12 §2.1, commande <c>test-api</c>) : un diagnostic
/// <see cref="PlatformProbeStatus"/> et un message opérateur français prêt à afficher.
/// </summary>
internal sealed class PlatformProbeResult
{
    public PlatformProbeResult(PlatformProbeStatus status, string message)
    {
        Status = status;
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }

    /// <summary>Diagnostic synthétique.</summary>
    public PlatformProbeStatus Status { get; }

    /// <summary>Message opérateur français.</summary>
    public string Message { get; }

    /// <summary>Vrai si la plateforme est joignable et la clé acceptée.</summary>
    public bool Success => Status == PlatformProbeStatus.Ok;
}
