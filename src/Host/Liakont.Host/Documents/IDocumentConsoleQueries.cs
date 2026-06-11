namespace Liakont.Host.Documents;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Documents.Contracts.DTOs;

/// <summary>
/// Composition en LECTURE de la page Documents de la console (WEB02) : charge, pour le tenant courant,
/// l'INTÉGRALITÉ des documents d'une période. Isole le chargement hors de la page Blazor (la page reste
/// présentationnelle, CLAUDE.md n°19) et le rend testable unitairement. Tenant-scopée (CLAUDE.md n°9 :
/// la connexion EST le tenant, aucune lecture cross-tenant possible).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Liakont.Modules.Documents.Contracts.Queries.IDocumentQueries.GetDocumentsAsync"/> est
/// paginée serveur (PageSize borné à 200). <see cref="DeclaredListPage{TItem}"/> du socle Stratum — le
/// gabarit OBLIGATOIRE de la console (aucune grille « maison ») — est CLIENT-paginé : il reçoit la liste
/// complète et assure pagination, filtres avancés, export et colonnes en mémoire. Ce service ponte les
/// deux en bouclant sur les pages serveur jusqu'à charger tout le périmètre, SANS troncature silencieuse
/// (une troncature serait un faux-vert). La période (défaut = mois courant, posé par la page) borne le
/// volume, comme le gabarit AdminAgents qui charge tous ses agents.
/// </para>
/// </remarks>
internal interface IDocumentConsoleQueries
{
    /// <summary>
    /// Charge tous les documents du tenant courant dont la date d'émission est dans [<paramref name="from"/>,
    /// <paramref name="to"/>] (bornes incluses ; <c>null</c> = pas de borne). Triés par dernière mise à jour
    /// décroissante (ordre du module).
    /// </summary>
    Task<IReadOnlyList<DocumentSummaryDto>> GetDocumentsInPeriodAsync(
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default);
}
