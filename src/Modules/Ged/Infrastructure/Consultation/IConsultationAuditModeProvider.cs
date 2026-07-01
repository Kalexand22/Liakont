namespace Liakont.Modules.Ged.Infrastructure.Consultation;

using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Ged.Contracts.Consultation;

/// <summary>
/// Résout le <see cref="ConsultationAuditMode"/> du tenant courant (ADR-0036 §3). SEAM de capacité tenant : le
/// régime probant (<see cref="ConsultationAuditMode.Evidential"/>) est GRAVÉ mais son ACTIVATION est du paramétrage
/// (D8, owner Sécurité + DPO). Le défaut (<see cref="DefaultConsultationAuditModeProvider"/>) renvoie
/// <see cref="ConsultationAuditMode.BestEffort"/> ; activer le régime probant se fait en substituant
/// l'implémentation (lecture d'une capacité tenant en base), SANS réécrire le writer.
/// </summary>
internal interface IConsultationAuditModeProvider
{
    ValueTask<ConsultationAuditMode> GetModeAsync(CancellationToken cancellationToken = default);
}
