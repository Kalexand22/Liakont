namespace Liakont.Host.MultiTenancy;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.TenantSettings.Contracts.Queries;

/// <summary>
/// Sonde « le tenant a-t-il DÉJÀ du paramétrage ? », utilisée par la garde provisioning <b>create-only</b>
/// (endpoint d'import de seed + amorçage de DEV). Depuis BUG-14, l'identité légale n'est plus seedée : le
/// profil ne marque donc plus « tenant paramétré ». La garde s'ancre sur la présence d'un composant de
/// PARAMÉTRAGE — fiscal, planification, seuils OU compte PA — chacun pouvant être le premier (et l'unique)
/// composant écrit par un seed donné (les blocs <c>tenant-profile.json</c> sont tous facultatifs). S'ancrer
/// sur le seul fiscal laisserait un seed sans bloc <c>fiscal</c> écraser silencieusement des réglages
/// saisis via la console (un ré-import n'est jamais destructif — CFG02 / OPS03). Toutes les lectures sont
/// tenant-scopées par <paramref name="companyId"/> (CLAUDE.md n°9/17).
/// </summary>
internal static class TenantConfigurationProbe
{
    public static async Task<bool> HasAnyConfigurationAsync(
        this ITenantSettingsQueries queries,
        Guid companyId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(queries);

        if (await queries.GetFiscalSettings(companyId, ct).ConfigureAwait(false) is not null)
        {
            return true;
        }

        if (await queries.GetExtractionSchedule(companyId, ct).ConfigureAwait(false) is not null)
        {
            return true;
        }

        if (await queries.GetAlertThresholds(companyId, ct).ConfigureAwait(false) is not null)
        {
            return true;
        }

        var paAccounts = await queries.GetPaAccounts(companyId, ct).ConfigureAwait(false);
        return paAccounts.Count > 0;
    }
}
