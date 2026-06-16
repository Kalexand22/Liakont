namespace Liakont.Modules.Documents.Contracts.Lifecycle;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Écriture (APPEND-ONLY) d'un fait d'audit de JOURNALISATION D'ENVOI PA (item FX06/FX07, F16 §7) : après
/// une transmission réussie d'un Factur-X par la PA générique, le pipeline (FX07) consigne le compte /
/// plug-in PA, les horodatages requête/réponse, l'empreinte de l'artefact transmis, la clé d'idempotence
/// recherchable et la réponse PA. Contrat SÉGRÉGÉ — symétrique au read <see cref="Queries.IPaTransmissionJournalQueries"/>
/// (même discipline que le précédent FIX212) : la journalisation n'alourdit pas le port de transitions
/// <see cref="IDocumentLifecycle"/>. Tenant-scopé (la connexion EST le tenant, database-per-tenant),
/// ATOMIQUE et APPEND-ONLY : aucun chemin update/delete sur <c>documents.document_events</c> (CLAUDE.md
/// n°4). Seule surface Contracts par laquelle un autre module consigne ce fait — aucun module n'écrit un
/// DocumentEvent directement (frontière Contracts-only, module-rules §3).
/// </summary>
public interface IPaTransmissionJournal
{
    /// <summary>
    /// Consigne, en append-only, l'envoi d'un artefact à la PA pour un document déjà émis. N'effectue
    /// AUCUNE transition d'état (la machine à états relève de <see cref="IDocumentLifecycle"/>) : un pur
    /// ajout d'événement d'audit. Lève si <paramref name="entry"/> est incomplet.
    /// </summary>
    /// <param name="entry">Données de la transmission journalisée (compte/plug-in PA, horodatages, empreinte, clé d'idempotence, réponse).</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    Task JournalAsync(PaTransmissionJournalEntry entry, CancellationToken cancellationToken = default);
}
