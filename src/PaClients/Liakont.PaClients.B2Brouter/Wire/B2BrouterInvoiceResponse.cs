namespace Liakont.PaClients.B2Brouter.Wire;

/// <summary>
/// Réponse B2Brouter à un import / une relecture de facture (F05 §3). DTO PROPRIÉTAIRE, <c>internal</c>.
/// ⚠️ Une réponse HTTP 200 PEUT contenir un <see cref="Errors"/> non vide (erreur silencieuse, F05 §4.1) :
/// la présence d'erreurs est inspectée par le mapper INDÉPENDAMMENT du code HTTP.
/// </summary>
internal sealed record B2BrouterInvoiceResponse
{
    /// <summary>Identifiant attribué par B2Brouter, ou <c>null</c> si l'import n'a pas abouti.</summary>
    public string? Id { get; init; }

    /// <summary>
    /// Numéro de document (EN 16931 BT-1) renvoyé par B2Brouter. Clé d'unicité côté PA (F05 §4.2) :
    /// sert à RACCROCHER une facture déjà créée lors de la relecture d'idempotence après un échec
    /// transitoire d'un POST (relecture de la liste du compte, F05 §4.2).
    /// </summary>
    public string? Number { get; init; }

    /// <summary>État B2Brouter du document (<c>new</c> / <c>sending</c> / <c>issued</c> / <c>error</c>…), F05 §3.</summary>
    public string? State { get; init; }

    /// <summary>Erreurs remontées par B2Brouter (même sur HTTP 200 — F05 §4.1), jamais perdues.</summary>
    public IReadOnlyList<B2BrouterError>? Errors { get; init; }

    /// <summary>Identifiants des tax reports liés (F05 §3).</summary>
    public IReadOnlyList<string>? TaxReportIds { get; init; }
}
