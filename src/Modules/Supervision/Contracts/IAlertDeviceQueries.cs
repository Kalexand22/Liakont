namespace Liakont.Modules.Supervision.Contracts;

using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Supervision.Contracts.DTOs;

/// <summary>
/// Lecture du DISPOSITIF d'alerte de supervision du tenant courant (FIX210, F12 §5) : règles actives et
/// gelées avec leur seuil effectif, état de l'e-mail opérateur, cadence d'évaluation. Tenant-scopée (le seuil
/// effectif vient des seuils du tenant — CFG02 ; jamais de lecture cross-tenant, CLAUDE.md n°9). N'expose
/// aucun secret ni aucune adresse e-mail (CLAUDE.md n°10).
/// </summary>
public interface IAlertDeviceQueries
{
    /// <summary>Assemble l'état du dispositif d'alerte du tenant courant.</summary>
    Task<AlertDeviceStatusDto> GetDeviceStatusAsync(CancellationToken cancellationToken = default);
}
