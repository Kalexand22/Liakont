namespace Liakont.Modules.Documents.Contracts.Queries;

using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Documents.Contracts.DTOs;

/// <summary>
/// Recherche d'un fait d'audit de journalisation d'envoi PA par sa CLÉ D'IDEMPOTENCE (item FX06, F16 §7 :
/// « clé d'idempotence recherchable »). Interface SÉGRÉGÉE (précédent FIX212) : la lecture d'idempotence de
/// transmission n'alourdit pas le contrat principal <see cref="IDocumentQueries"/>. Consommée par le pipeline
/// (FX07) pour éviter une double-journalisation/un double-envoi, et par la restitution de support. Tenant-scopée
/// par la connexion (database-per-tenant) — aucune lecture cross-tenant (CLAUDE.md n°9/17).
/// </summary>
public interface IPaTransmissionJournalQueries
{
    /// <summary>
    /// Retrouve le fait de journalisation d'envoi PA le plus récent portant la clé d'idempotence donnée, ou
    /// <c>null</c> si aucun envoi n'a été journalisé pour cette clé. Sert l'index partiel
    /// <c>ix_document_events_idempotency_key</c>.
    /// </summary>
    /// <param name="idempotencyKey">La clé d'idempotence recherchée.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <returns>La projection de l'envoi journalisé, ou <c>null</c>.</returns>
    Task<PaTransmissionJournalDto?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);
}
