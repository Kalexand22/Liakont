namespace Liakont.Host.PaAccounts;

using System;
using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Construit la demande de réglage de tax report (<see cref="PaTaxReportSettingRequest"/>) à partir des
/// valeurs de publication FOURNIES (opérateur ou seed) — JAMAIS inventées (CLAUDE.md n°2/7). Le SIREN
/// n'est pas un champ de la demande : le compte côté PA EST celui du SIREN du tenant, et le réglage est
/// assigné au NIVEAU SIREN via <c>cin_scheme « 0002 »</c> (pas SIRET — F05 §2). Source unique de cette
/// constante de spec, partagée par l'action console et le seed de dev pour publier un réglage cohérent.
/// </summary>
internal static class TaxReportSettingRequestBuilder
{
    /// <summary>Schéma d'identification du compte au niveau SIREN (F05 §2 — constante de spec, pas une donnée client).</summary>
    public const string SirenCinScheme = "0002";

    /// <summary>
    /// Construit la demande. <paramref name="startDate"/>, <paramref name="typeOperation"/> et
    /// <paramref name="enterpriseSize"/> proviennent du paramétrage du tenant / de la saisie opérateur.
    /// </summary>
    /// <param name="startDate">Date de début de publication à déclarer.</param>
    /// <param name="typeOperation">Type d'opération à déclarer côté PA.</param>
    /// <param name="enterpriseSize">Taille d'entreprise à déclarer côté PA.</param>
    /// <param name="nafCode">Code NAF/INSEE facultatif (vide/blanc = non déclaré).</param>
    public static PaTaxReportSettingRequest Build(
        DateOnly startDate,
        string typeOperation,
        string enterpriseSize,
        string? nafCode) => new()
        {
            StartDate = startDate,
            TypeOperation = typeOperation,
            EnterpriseSize = enterpriseSize,
            NafCode = string.IsNullOrWhiteSpace(nafCode) ? null : nafCode.Trim(),

            // Le SIREN du tenant identifie le compte au niveau SIREN (F05 §2) : cin_scheme figé « 0002 ».
            CinScheme = SirenCinScheme,
        };
}
