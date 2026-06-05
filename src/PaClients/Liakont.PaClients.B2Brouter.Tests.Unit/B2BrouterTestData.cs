namespace Liakont.PaClients.B2Brouter.Tests.Unit;

using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Fabriques de documents pivot et d'un <see cref="B2BrouterClient"/> piloté par un mock HTTP, pour
/// les tests de PAB01. Montants en <see cref="decimal"/> (CLAUDE.md n°1), valeurs FICTIVES (aucune
/// donnée client — CLAUDE.md n°7). Le mapping TVA (catégorie/taux/VATEX) est posé tel que la
/// PLATEFORME (F03) l'enrichirait dans le pivot — le plug-in le recopie sans rien inventer.
/// </summary>
internal static class B2BrouterTestData
{
    /// <summary>Réponse B2Brouter d'un envoi accepté (HTTP 200 + état issued).</summary>
    public const string IssuedJson = """{"id":"INV-1001","state":"issued","tax_report_ids":["TR-1"]}""";

    /// <summary>Liste de factures VIDE (forme tableau nu) — relecture d'idempotence : numéro absent.</summary>
    public const string EmptyInvoiceListJson = "[]";

    /// <summary>
    /// Liste de factures (forme tableau nu) contenant UNE facture pour le numéro donné — relecture
    /// d'idempotence : numéro déjà créé côté PA (raccrochage).
    /// </summary>
    /// <param name="number">Numéro de document recherché.</param>
    /// <param name="id">Identifiant attribué par la PA.</param>
    /// <param name="state">État B2Brouter (issued / sending / new / error).</param>
    public static string InvoiceListJsonWith(string number, string id = "INV-RECONNECT", string state = "issued") =>
        $$"""[{"id":"{{id}}","number":"{{number}}","state":"{{state}}","tax_report_ids":["TR-9"]}]""";

    /// <summary>Même liste mais sous la forme ENVELOPPÉE <c>{ "invoices": [...] }</c> (variante tolérée).</summary>
    /// <param name="number">Numéro de document recherché.</param>
    /// <param name="id">Identifiant attribué par la PA.</param>
    /// <param name="state">État B2Brouter.</param>
    public static string WrappedInvoiceListJsonWith(string number, string id = "INV-WRAPPED", string state = "issued") =>
        $$"""{"invoices":[{"id":"{{id}}","number":"{{number}}","state":"{{state}}"}]}""";

    /// <summary>Facture B2C simple à 20 % (une ligne, catégorie S).</summary>
    public static PivotDocumentDto Invoice20(string number = "F-2026-001") => new(
        sourceDocumentKind: "FACTURE",
        number: number,
        issueDate: new DateTime(2026, 1, 15),
        sourceReference: $"SRC-{number}",
        supplier: new PivotPartyDto("SVV Démo", siren: "123456789"),
        totals: new PivotTotalsDto(100m, 20m, 120m),
        operationCategory: OperationCategory.LivraisonBiens,
        lines: [new PivotLineDto("Prestation", 100m, taxes: [new PivotLineTaxDto(20m, 20m, VatCategory.S)])]);

    /// <summary>
    /// Adjudication au régime de la marge — modèle « 2 lignes » validé en staging (F03 §2.3) :
    /// adjudication (E, 0 %, VATEX-EU-J) + frais acheteur (S, 20 %).
    /// </summary>
    public static PivotDocumentDto MarginTwoLines(string number = "F-2026-002") => new(
        sourceDocumentKind: "FACTURE",
        number: number,
        issueDate: new DateTime(2026, 3, 10),
        sourceReference: $"SRC-{number}",
        supplier: new PivotPartyDto("SVV Démo", siren: "123456789"),
        totals: new PivotTotalsDto(1200m, 40m, 1240m),
        operationCategory: OperationCategory.Mixte,
        lines:
        [
            new PivotLineDto(
                "Adjudication (bien d'occasion)",
                1000m,
                taxes: [new PivotLineTaxDto(0m, 0m, VatCategory.E, "VATEX-EU-J")]),
            new PivotLineDto(
                "Frais acheteur",
                200m,
                taxes: [new PivotLineTaxDto(40m, 20m, VatCategory.S)]),
        ]);

    /// <summary>
    /// Avoir rattaché à une facture d'origine. Montants POSITIFS : l'avoir est stocké en positif côté
    /// source (F07-F08), et B2Brouter matérialise la rectification par le flag <c>is_credit_note</c>,
    /// PAS par un signe négatif — combiner négatif + is_credit_note inverserait deux fois le signe. Le
    /// plug-in recopie les montants sans manipuler le signe (il n'invente rien — CLAUDE.md n°2).
    /// </summary>
    public static PivotDocumentDto CreditNote(string number = "A-2026-001") => new(
        sourceDocumentKind: "AVOIR",
        number: number,
        issueDate: new DateTime(2026, 2, 1),
        sourceReference: $"SRC-{number}",
        supplier: new PivotPartyDto("SVV Démo", siren: "123456789"),
        totals: new PivotTotalsDto(50m, 10m, 60m),
        operationCategory: OperationCategory.LivraisonBiens,
        lines: [new PivotLineDto("Remboursement", 50m, taxes: [new PivotLineTaxDto(10m, 20m, VatCategory.S)])],
        creditNoteRefs: [new PivotDocumentRefDto("F-ORIGINE", new DateTime(2026, 1, 10))]);

    /// <summary>
    /// Crée un client B2Brouter piloté par un handler mocké (URL staging par défaut). Le backoff est mis
    /// à ZÉRO par défaut (<see cref="B2BrouterRetryPolicy.NoDelay"/>) pour que la boucle de retry/idempotence
    /// (PAB02) s'exerce sans attente réelle ; un test peut fournir sa propre politique.
    /// </summary>
    /// <param name="handler">Handler HTTP de test.</param>
    /// <param name="capabilities">Capacités à déclarer ; <c>null</c> = capacités nominales du plug-in.</param>
    /// <param name="accountId">Identifiant de compte (segment d'URL).</param>
    /// <param name="retryPolicy">Politique de retry ; <c>null</c> = 3 réessais sans délai (backoff zéro).</param>
    public static B2BrouterClient CreateClient(
        HttpMessageHandler handler,
        PaCapabilities? capabilities = null,
        string accountId = "ACC-DEMO",
        B2BrouterRetryPolicy? retryPolicy = null)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri(B2BrouterDefaults.StagingBaseUrl) };
        return new B2BrouterClient(
            http,
            new B2BrouterClientOptions(accountId, capabilities, retryPolicy ?? B2BrouterRetryPolicy.NoDelay()));
    }
}
