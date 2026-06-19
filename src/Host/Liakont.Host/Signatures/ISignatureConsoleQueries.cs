namespace Liakont.Host.Signatures;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.DocumentApproval.Contracts;

/// <summary>
/// Composition en LECTURE de la page console des signatures (SIG10, F17 §0). Pendant en lecture de
/// <see cref="ISignatureConsoleActions"/> : la console s'exécute dans son circuit serveur (InteractiveServer)
/// et appelle ce service IN-PROCESS (elle ne ré-emprunte pas son propre endpoint HTTP — précédent WEB05).
/// Isole l'orchestration hors de la page Blazor (présentationnelle, CLAUDE.md n°19) et la rend testable.
/// TENANT-SCOPÉ : le <c>company_id</c> est résolu côté serveur depuis le contexte d'acteur, jamais fourni
/// par le client (CLAUDE.md n°9/17).
/// </summary>
internal interface ISignatureConsoleQueries
{
    /// <summary>
    /// Lit l'état de validation d'un document pour une finalité (tentative la plus récente + journal append-only),
    /// scopé au tenant courant. Ne lève PAS sur l'absence de tentative (<see cref="SignatureStatusView.Latest"/>
    /// vaut alors <c>null</c>) ; lève si le tenant n'est pas résolu (bloquer plutôt que lire faux, CLAUDE.md n°3/9).
    /// </summary>
    Task<SignatureStatusView> GetStatusAsync(Guid documentId, ValidationPurpose purpose, CancellationToken cancellationToken = default);

    /// <summary>
    /// Types de fournisseurs de signature actuellement enregistrés par le Host (diagnostic opérateur). Une
    /// collection vide est un état VALIDE : la signature est optionnelle, l'acceptation enregistrée (Recorded)
    /// reste toujours disponible (INV-SIGPROV-6).
    /// </summary>
    IReadOnlyCollection<string> GetConfiguredProviderTypes();
}
