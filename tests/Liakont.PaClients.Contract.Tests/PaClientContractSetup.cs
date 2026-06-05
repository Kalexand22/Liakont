namespace Liakont.PaClients.Contract.Tests;

using System.Collections.Generic;
using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Demande de configuration d'un client PA pour un cas de contrat — la seule entrée que la suite de
/// contrat (<see cref="PaClientContractTests"/>) passe à un plug-in via
/// <c>PaClientContractTests.CreateClient</c>. Découple le « QUOI » (l'issue + les capacités à tester)
/// du « COMMENT » (Fake en mémoire, mock HTTP B2Brouter…), pour qu'un seul jeu de tests s'applique à
/// tout plug-in (blueprint.md §2 règle 3).
/// </summary>
public sealed record PaClientContractSetup
{
    /// <summary>Issue que les envois du client doivent produire (succès par défaut).</summary>
    public PaSendOutcome Outcome { get; init; } = PaSendOutcome.Success;

    /// <summary>
    /// Capacités que le client doit DÉCLARER, ou <c>null</c> pour les capacités nominales du plug-in.
    /// La suite s'en sert pour vérifier l'invariant « capacité absente → résultat typé, jamais
    /// d'exception » (PAA01) en retirant une capacité des capacités nominales.
    /// </summary>
    public PaCapabilities? Capabilities { get; init; }

    /// <summary>
    /// Erreurs métier à remonter pour les issues <see cref="PaSendOutcome.Rejected"/> et
    /// <see cref="PaSendOutcome.SilentError"/>, ou <c>null</c> pour les erreurs par défaut du plug-in.
    /// La suite vérifie qu'elles remontent INTACTES (F05 §3).
    /// </summary>
    public IReadOnlyList<PaError>? RejectionErrors { get; init; }
}
