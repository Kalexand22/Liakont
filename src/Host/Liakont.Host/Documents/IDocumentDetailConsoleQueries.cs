namespace Liakont.Host.Documents;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Lecture d'assemblage de la page détail document pour la console (WEB03a). Compose, en UNE vue, l'en-tête,
/// la piste d'audit, le motif de blocage courant et la référence d'archive d'un document à partir du module
/// Documents (TRK01). <c>internal</c> : strictement interne au Host (la console n'expose pas de contrat).
/// </summary>
internal interface IDocumentDetailConsoleQueries
{
    /// <summary>
    /// Détail assemblé du document <paramref name="id"/> dans le tenant courant, ou <c>null</c> s'il n'existe
    /// pas (la page rend alors un « document introuvable »). Tenant-scopé par construction.
    /// </summary>
    Task<DocumentDetailViewModel?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default);
}
