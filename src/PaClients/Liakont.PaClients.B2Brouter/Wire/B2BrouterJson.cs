namespace Liakont.PaClients.B2Brouter.Wire;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Options de sérialisation JSON du plug-in B2Brouter, mises en cache (CA1869). Convention snake_case
/// (<c>send_after_import</c>, <c>is_credit_note</c>, <c>amended_number</c>, <c>invoice_lines</c>,
/// <c>tax_report_ids</c>…) attendue par l'API B2Brouter (F05). Les champs nuls sont OMIS (un avoir
/// renseigne <c>amended_*</c>, une facture normale ne les émet pas). System.Text.Json sérialise les
/// <see cref="decimal"/> en nombres JSON sans perte — aucun <c>double</c> sur un montant (CLAUDE.md n°1).
/// </summary>
internal static class B2BrouterJson
{
    /// <summary>Options partagées (insensibles à la casse en lecture, snake_case en écriture).</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
