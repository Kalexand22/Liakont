namespace Liakont.Modules.Pipeline.Infrastructure.Send;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Compose la preuve d'échange avec la Plateforme Agréée en JSON VALIDE (la colonne <c>pa_response_snapshot</c>
/// de la piste d'audit est <c>jsonb</c>, et le fichier <c>reponse-pa.json</c> du paquet WORM est lisible).
/// La réponse brute de la PA — qui peut N'ÊTRE PAS du JSON (texte, XML, message d'erreur) — est EMBARQUÉE
/// telle quelle dans un champ chaîne <c>rawResponse</c> (échappée), jamais réinterprétée : on conserve la
/// preuve intacte sans jamais produire un snapshot mal formé.
/// </summary>
internal static class SendPaSnapshot
{
    /// <summary>Snapshot JSON valide d'un résultat d'envoi (état, identifiant PA, erreurs, réponse brute embarquée).</summary>
    public static string FromSendResult(PaSendResult result) =>
        Serialize(result.State.ToString(), result.PaDocumentId, result.TaxReportIds, result.Errors, result.RawResponse);

    /// <summary>Snapshot JSON valide d'un statut PA relu (anti-doublon : la PA confirme l'émission).</summary>
    public static string FromStatus(PaDocumentStatus status) =>
        Serialize(status.State.ToString(), status.PaDocumentId, status.TaxReportIds, status.Errors, status.RawResponse);

    private static string Serialize(
        string state,
        string? paDocumentId,
        IReadOnlyList<string> taxReportIds,
        IReadOnlyList<PaError> errors,
        string? rawResponse)
    {
        return JsonSerializer.Serialize(new
        {
            state,
            paDocumentId,
            taxReportIds,
            errors = errors.Select(error => new { error.Code, error.Message }).ToList(),
            rawResponse,
        });
    }
}
