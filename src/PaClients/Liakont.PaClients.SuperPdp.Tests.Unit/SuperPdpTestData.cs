namespace Liakont.PaClients.SuperPdp.Tests.Unit;

using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Fabriques de documents pivot et d'un <see cref="SuperPdpClient"/> piloté par un mock HTTP + un
/// fournisseur de jeton de test, pour les tests de PAS02. Montants en <see cref="decimal"/> (CLAUDE.md
/// n°1), valeurs FICTIVES (aucune donnée client — CLAUDE.md n°7). Le mapping TVA (catégorie/taux/VATEX)
/// est posé tel que la PLATEFORME (F03) l'enrichirait dans le pivot — le plug-in le recopie sans rien
/// inventer. Les corps de réponse reproduisent le contrat RÉEL (✅ OpenAPI + sandbox 2026-06-12, F14
/// §3.2/§3.4) : ressource <c>invoice</c> avec <c>events[]</c>, liste <c>{"data":[…]}</c>, erreur
/// <c>{"http_status_code","message"}</c>.
/// </summary>
internal static class SuperPdpTestData
{
    /// <summary>XML CII rendu par la conversion (le client le fait suivre sans le parser — F14 §3.2).</summary>
    public const string CiiXml =
        """<?xml version="1.0" encoding="UTF-8"?><CrossIndustryInvoice xmlns="urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100"/>""";

    /// <summary>
    /// Réponse d'une émission ABOUTIE : la ressource invoice avec le cycle observé en sandbox jusqu'à
    /// <c>fr:201</c> « Émise par la plateforme » → <see cref="PaSendState.Issued"/> (F14 §4.1).
    /// </summary>
    public const string IssuedJson =
        """{"id":1001,"company_id":12085,"direction":"out","external_id":"CT-1","events":[{"status_code":"api:uploaded","status_text":"Téléversée"},{"status_code":"fr:200","status_text":"Déposée (validée)"},{"status_code":"fr:201","status_text":"Émise par la plateforme"}]}""";

    /// <summary>
    /// Réponse SYNCHRONE typique du POST réel : seulement <c>api:uploaded</c> (téléversée, envoi
    /// asynchrone en file) → <see cref="PaSendState.Sending"/>, JAMAIS « émise » (F14 §4.1).
    /// </summary>
    public const string UploadedJson =
        """{"id":1002,"company_id":12085,"direction":"out","external_id":"CT-UP","events":[{"status_code":"api:uploaded","status_text":"Téléversée"}]}""";

    /// <summary>Liste de factures VIDE (forme réelle <c>{"data":[…]}</c>) — relecture d'idempotence : clé absente.</summary>
    public const string EmptyInvoiceListJson = """{"data":[],"count":0,"has_before":false,"has_after":false}""";

    /// <summary>
    /// Liste de factures (forme réelle <c>{"data":[…]}</c>) contenant UNE facture pour l'<c>external_id</c>
    /// donné — relecture d'idempotence : la facture a déjà été créée côté PA (raccrochage, F14 §4.1).
    /// </summary>
    /// <param name="externalId">Clé d'idempotence recherchée (le numéro de document).</param>
    /// <param name="id">Identifiant numérique attribué par la PA.</param>
    /// <param name="statusCode">Code du dernier événement (ex. <c>fr:201</c> émise / <c>api:uploaded</c> en cours).</param>
    public static string InvoiceListJsonWith(string externalId, long id = 2001, string statusCode = "fr:201") =>
        $$"""{"data":[{"id":{{id}},"direction":"out","external_id":"{{externalId}}","events":[{"status_code":"{{statusCode}}","status_text":"Statut simulé"}]}],"count":1,"has_before":false,"has_after":false}""";

    /// <summary>Corps d'erreur Super PDP (✅ format réel confirmé sandbox — F14 §4.1).</summary>
    /// <param name="httpStatusCode">Code HTTP répété dans le corps.</param>
    /// <param name="message">Message Super PDP (conservé intact par le mapper).</param>
    public static string ErrorJson(int httpStatusCode, string message) =>
        $$"""{"http_status_code":{{httpStatusCode}},"message":"{{message}}"}""";

    /// <summary>Facture simple à 20 % (une ligne, catégorie S), destinataire IDENTIFIÉ (SIREN fictif).</summary>
    public static PivotDocumentDto Invoice20(string number = "F-2026-001") => new(
        sourceDocumentKind: "FACTURE",
        number: number,
        issueDate: new DateTime(2026, 1, 15),
        sourceReference: $"SRC-{number}",
        supplier: new PivotPartyDto("SVV Démo", siren: "123456789", vatNumber: "FR32123456789"),
        totals: new PivotTotalsDto(100m, 20m, 120m),
        operationCategory: OperationCategory.LivraisonBiens,
        customer: new PivotPartyDto("Client Démo", siren: "987654321"),
        lines: [new PivotLineDto("Prestation", 100m, taxes: [new PivotLineTaxDto(20m, 20m, VatCategory.S)])]);

    /// <summary>
    /// Facture à ÉCHÉANCE NON SOLDÉE : montant dû POSITIF (aucun acompte) + date d'échéance de paiement
    /// (EN 16931 BT-9, EXT01). Sert à vérifier l'émission de <c>payment_due_date</c> — le cas que BR-CO-25
    /// exigeait et que le pivot ne portait pas avant EXT01 (F14 §3.2/O11).
    /// </summary>
    /// <param name="number">Numéro du document (clé d'idempotence).</param>
    /// <param name="dueDate">Date d'échéance de paiement (BT-9).</param>
    public static PivotDocumentDto Invoice20WithDueDate(string number, DateTime dueDate) => new(
        sourceDocumentKind: "FACTURE",
        number: number,
        issueDate: new DateTime(2026, 1, 15),
        sourceReference: $"SRC-{number}",
        supplier: new PivotPartyDto("SVV Démo", siren: "123456789", vatNumber: "FR32123456789"),
        totals: new PivotTotalsDto(100m, 20m, 120m),
        operationCategory: OperationCategory.LivraisonBiens,
        customer: new PivotPartyDto("Client Démo", siren: "987654321"),
        lines: [new PivotLineDto("Prestation", 100m, taxes: [new PivotLineTaxDto(20m, 20m, VatCategory.S)])],
        paymentDueDate: dueDate);

    /// <summary>Même facture mais SANS destinataire : exerce la garde locale d'adressage (F14 §3.2).</summary>
    public static PivotDocumentDto Invoice20WithoutCustomer(string number = "F-2026-009") => new(
        sourceDocumentKind: "FACTURE",
        number: number,
        issueDate: new DateTime(2026, 1, 15),
        sourceReference: $"SRC-{number}",
        supplier: new PivotPartyDto("SVV Démo", siren: "123456789", vatNumber: "FR32123456789"),
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
        supplier: new PivotPartyDto("SVV Démo", siren: "123456789", vatNumber: "FR32123456789"),
        totals: new PivotTotalsDto(1200m, 40m, 1240m),
        operationCategory: OperationCategory.Mixte,
        customer: new PivotPartyDto("Client Démo", siren: "987654321"),
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
    /// Avoir rattaché à une facture d'origine. Montants POSITIFS (l'avoir est stocké en positif côté
    /// source, F07-F08) : sert à vérifier que la garde de capacité <c>SupportsCreditNotes</c> = false
    /// dégrade l'avoir en résultat typé (V1 Super PDP n'émet pas d'avoir — F14 §5).
    /// </summary>
    public static PivotDocumentDto CreditNote(string number = "A-2026-001") => new(
        sourceDocumentKind: "AVOIR",
        number: number,
        issueDate: new DateTime(2026, 2, 1),
        sourceReference: $"SRC-{number}",
        supplier: new PivotPartyDto("SVV Démo", siren: "123456789", vatNumber: "FR32123456789"),
        totals: new PivotTotalsDto(50m, 10m, 60m),
        operationCategory: OperationCategory.LivraisonBiens,
        customer: new PivotPartyDto("Client Démo", siren: "987654321"),
        lines: [new PivotLineDto("Remboursement", 50m, taxes: [new PivotLineTaxDto(10m, 20m, VatCategory.S)])],
        creditNoteRefs: [new PivotDocumentRefDto("F-ORIGINE", new DateTime(2026, 1, 10))]);

    /// <summary>
    /// Crée un client Super PDP piloté par un handler mocké (URL sandbox par défaut) et un fournisseur de
    /// jeton de test (pas d'aller-retour OAuth réel — l'OAuth est testé séparément). Le backoff est mis à
    /// ZÉRO par défaut (<see cref="SuperPdpRetryPolicy.NoDelay"/>) pour exercer la boucle de retry sans
    /// attente réelle ; un test peut fournir sa propre politique ou son propre fournisseur de jeton.
    /// </summary>
    /// <param name="handler">Handler HTTP de test.</param>
    /// <param name="capabilities">Capacités à déclarer ; <c>null</c> = capacités nominales du plug-in.</param>
    /// <param name="tokenProvider">Fournisseur de jeton ; <c>null</c> = <see cref="StubTokenProvider"/> nominal.</param>
    /// <param name="retryPolicy">Politique de retry ; <c>null</c> = 3 réessais sans délai (backoff zéro).</param>
    /// <param name="accountId">Identifiant de compte (audit).</param>
    public static SuperPdpClient CreateClient(
        HttpMessageHandler handler,
        PaCapabilities? capabilities = null,
        ISuperPdpTokenProvider? tokenProvider = null,
        SuperPdpRetryPolicy? retryPolicy = null,
        string accountId = "ACC-DEMO")
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri(SuperPdpDefaults.SandboxBaseUrl) };
        return new SuperPdpClient(
            http,
            tokenProvider ?? new StubTokenProvider(),
            new SuperPdpClientOptions(accountId, capabilities, retryPolicy ?? SuperPdpRetryPolicy.NoDelay()));
    }
}
