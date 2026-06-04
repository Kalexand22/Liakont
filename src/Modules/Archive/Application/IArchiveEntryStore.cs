namespace Liakont.Modules.Archive.Application;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Persistance des entrées du coffre dans <c>documents.archive_entries</c> (table créée par TRK01,
/// « alimentée par TRK05 »), tenant-scopée par la connexion. Garante de l'ordonnancement STRICT et de
/// l'atomicité du maillon de chaîne : <see cref="AppendAsync"/> sérialise les ajouts du tenant, lit la
/// tête de chaîne, calcule le <c>chain_hash</c>, fait écrire les artefacts du coffre (qui scellent ce
/// <c>chain_hash</c> dans leur manifest) PUIS insère la ligne — le tout dans une seule transaction.
/// </summary>
public interface IArchiveEntryStore
{
    /// <summary>
    /// Ajoute une entrée à la chaîne du tenant. Sous verrou de sérialisation : lit la tête, dérive le
    /// contexte de scellement (<see cref="ArchiveSealContext"/> : <c>chain_hash</c> + horodatage strictement
    /// croissant), invoque <paramref name="writeArtifacts"/> pour écrire le paquet/addendum dans le coffre
    /// (qui RETOURNE le chemin du manifest scellé — un addendum ne connaît son numéro qu'une fois sous
    /// verrou), puis insère la ligne <c>documents.archive_entries</c>. Tout dans une seule transaction :
    /// si l'écriture du coffre échoue, aucune ligne n'est insérée.
    /// </summary>
    /// <param name="documentId">Document rattaché (FK).</param>
    /// <param name="packageHash">Empreinte du paquet/addendum (entry_hash) — stockée en <c>package_hash</c>.</param>
    /// <param name="writeArtifacts">
    /// Écrit les fichiers du coffre une fois le <c>chain_hash</c> connu et retourne le chemin (relatif au
    /// tenant) du manifest de l'entrée, stocké en <c>package_path</c>.
    /// </param>
    Task<ArchiveEntryRecord> AppendAsync(
        Guid documentId,
        string packageHash,
        Func<ArchiveSealContext, CancellationToken, Task<string>> writeArtifacts,
        CancellationToken cancellationToken = default);

    /// <summary>Lit toute la chaîne du tenant, dans l'ordre d'ajout (déterministe).</summary>
    Task<IReadOnlyList<ArchiveEntryRecord>> GetChainAsync(CancellationToken cancellationToken = default);
}
