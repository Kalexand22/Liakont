namespace Liakont.Modules.Ged.Infrastructure.Consultation;

using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Ged.Contracts.Consultation;

/// <summary>
/// Implémentation par DÉFAUT du seam <see cref="IConsultationAuditModeProvider"/> : renvoie toujours
/// <see cref="ConsultationAuditMode.BestEffort"/> (ADR-0036 §3, D8 non tranché). Aucune valeur probante présumée
/// tant que le tenant ne la confirme pas. Activer le régime probant = substituer une implémentation qui lit une
/// capacité tenant en base ; le writer n'a pas à changer (le mécanisme des deux régimes est déjà gravé).
/// </summary>
internal sealed class DefaultConsultationAuditModeProvider : IConsultationAuditModeProvider
{
    public ValueTask<ConsultationAuditMode> GetModeAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(ConsultationAuditMode.BestEffort);
}
