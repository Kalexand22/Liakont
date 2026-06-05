namespace Liakont.PaClients.B2Brouter.Wire;

/// <summary>
/// Enveloppe du corps d'écriture du réglage de tax report DGFiP — B2Brouter (Rails) attend
/// <c>{ "tax_report_setting": { … } }</c> en POST/PATCH (F05 §2, même convention d'enveloppe que
/// <c>{ "invoice": { … } }</c> pour l'envoi). DTO PROPRIÉTAIRE, <c>internal</c> ; sérialisé en
/// snake_case (<see cref="B2BrouterJson"/>), les champs nuls omis.
/// </summary>
internal sealed record B2BrouterTaxReportSettingRequest
{
    /// <summary>Le réglage à créer/mettre à jour.</summary>
    public required B2BrouterTaxReportSetting TaxReportSetting { get; init; }
}
