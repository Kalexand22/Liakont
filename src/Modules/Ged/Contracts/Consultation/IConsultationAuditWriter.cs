namespace Liakont.Modules.Ged.Contracts.Consultation;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Port d'écriture du journal de consultation GED (F19 §6.6, ADR-0036 §3). NEUF, tenant-scopé : n'écrit QUE dans
/// la base du tenant courant (schéma <c>ged_index</c>, routé par <c>IConnectionFactory</c>), JAMAIS dans l'audit
/// socle partagé (<c>ISystemConnectionFactory</c> = fuite cross-tenant). CONSOMMÉ par les pages / handlers
/// <c>/ged/*</c> : chaque opération du portail (recherche, fiche, exploration, export, ouverture de paquet) écrit
/// UNE entrée.
/// </summary>
/// <remarks>
/// Robustesse (§3) : selon le <see cref="ConsultationAuditMode"/> du tenant, le défaut <c>BestEffort</c> ne fait
/// JAMAIS échouer la lecture (échec journalisé en Warning), tandis que le régime <c>Evidential</c> est fail-closed
/// (l'échec de trace lève une exception que l'appelant traduit en refus d'accès). Confidentialité (§6.5) : le
/// masquage server-side de <see cref="ConsultationLogEntry.QueryText"/> et des valeurs confidentielles de
/// <see cref="ConsultationLogEntry.Detail"/> est appliqué par l'implémentation avant insertion (anti-oracle).
/// </remarks>
public interface IConsultationAuditWriter
{
    /// <summary>
    /// Écrit une entrée de consultation. En régime <c>BestEffort</c> : n'échoue jamais (une trace ratée n'annule
    /// pas la lecture). En régime <c>Evidential</c> : lève si la trace ne peut être écrite (fail-closed).
    /// </summary>
    Task WriteAsync(ConsultationLogEntry entry, CancellationToken cancellationToken = default);
}
