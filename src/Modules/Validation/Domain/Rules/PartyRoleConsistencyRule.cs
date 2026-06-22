namespace Liakont.Modules.Validation.Domain.Rules;

using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Validation.Contracts;
using Liakont.Modules.Validation.Domain.Identity;

/// <summary>
/// Cohérence des rôles de tiers PORTÉS PAR LE CONTRAT mais non couverts par les autres règles (RD404,
/// finding RD4-09). Le pivot porte <c>IsSelfBilled</c> / <c>Invoicer</c> (auto-facturation 389,
/// ADR-0004 D3-6) et <c>Payee</c> (affacturage, EN 16931 BG-10) ; le pipeline consomme déjà
/// <c>IsSelfBilled</c> (garde d'émission 389, MND07) mais AUCUNE règle n'en contrôlait la cohérence.
/// <para>
/// Contrôles d'INTÉGRITÉ de données (jamais une règle fiscale inventée — CLAUDE.md n°2). F15 §1.8 ferme
/// l'existence d'un contrôle CTC propre au 389 au-delà du type admis (G1.01) et de la numérotation
/// (G1.42/G1.45) ; l'identification de l'émetteur matériel relève des <b>règles de rôle générales</b>,
/// exactement comme l'identité émetteur/acheteur (VAL02). Cette règle ne fait que rendre cohérents les
/// champs que la source remplit :
/// </para>
/// <list type="bullet">
///   <item>une auto-facture (389) est, par définition, émise par un tiers DISTINCT du vendeur
///   (art. 289 I-2 CGI, F15 §1.1/§1.2) : <c>IsSelfBilled</c> ⇒ <c>Invoicer</c> présent ET identifié
///   (SIREN BT-30) — sinon BLOQUANT (CLAUDE.md n°3, « bloquer plutôt qu'envoyer faux ») ;</item>
///   <item>réciproquement, un émetteur de facture distinct du vendeur (<c>Invoicer</c>) relève de
///   l'auto-facturation / facturation pour compte de tiers (ADR-0004 D3-6) : <c>Invoicer</c> présent ⇒
///   <c>IsSelfBilled</c> cohérent — sinon BLOQUANT ;</item>
///   <item><c>Payee</c> (bénéficiaire de paiement, affacturage BG-10) est DIFFÉRÉ explicite (RD409,
///   addendum ADR-0004-bis) : aucun sérialiseur PA ne le projette en V1 ; sa présence est SIGNALÉE à
///   l'opérateur (Warning) pour ne pas laisser un champ contractuel passer pour transmis alors qu'il
///   est inerte — jamais bloquant (donnée optionnelle, pas un faux).</item>
/// </list>
/// Règle PURE : ne lit que le document, aucune dépendance externe.
/// </summary>
public sealed class PartyRoleConsistencyRule : IDocumentRule
{
    /// <summary>Document marqué auto-facturé (389) sans émetteur matériel (<c>Invoicer</c> absent).</summary>
    public const string SelfBilledInvoicerMissing = "SELF_BILLED_INVOICER_MISSING";

    /// <summary>Document auto-facturé (389) dont l'émetteur matériel (<c>Invoicer</c>) n'a pas de SIREN identifié/valide.</summary>
    public const string SelfBilledInvoicerUnidentified = "SELF_BILLED_INVOICER_UNIDENTIFIED";

    /// <summary>Émetteur matériel (<c>Invoicer</c>) présent mais document NON marqué auto-facturé (incohérence).</summary>
    public const string InvoicerWithoutSelfBilled = "INVOICER_WITHOUT_SELF_BILLED";

    /// <summary>Bénéficiaire de paiement (<c>Payee</c>, affacturage BG-10) présent mais NON transmis en V1 (différé explicite).</summary>
    public const string PayeeNotTransmitted = "PAYEE_NOT_TRANSMITTED";

    /// <inheritdoc/>
    public string Code => "PARTY_ROLE_CONSISTENCY";

    /// <inheritdoc/>
    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(DocumentValidationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var document = context.Document;
        var issues = new List<ValidationIssue>();

        AddSelfBillingIssues(document, issues);
        AddPayeeDeferralWarning(document, issues);

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }

    private static void AddSelfBillingIssues(PivotDocumentDto document, List<ValidationIssue> issues)
    {
        var invoicer = document.Invoicer;

        if (document.IsSelfBilled)
        {
            // 389 = « facture que le client/tiers produit à la place du vendeur » (UNTDID 1001, F15 §1.2) :
            // l'émetteur matériel (le mandataire) est, par construction, distinct du vendeur. Un 389 sans
            // Invoicer est donc internement incohérent (Invoicer null = identique au vendeur — contrat).
            if (invoicer is null)
            {
                issues.Add(ValidationIssue.Blocking(
                    SelfBilledInvoicerMissing,
                    $"Le document n° {document.Number} est marqué auto-facturé (389) mais ne porte aucun émetteur matériel. Une auto-facture sous mandat est émise par un tiers distinct du vendeur (art. 289 I-2 du CGI) : renseignez l'émetteur matériel (le mandataire) dans le logiciel source, ou retirez l'indicateur d'auto-facturation. Le document reste bloqué.",
                    "IsSelfBilled=true mais Invoicer absent (émetteur matériel non porté ; ADR-0004 D3-6, F15 §1.2).",
                    "BT-3"));
            }
            else if (!SirenValidator.IsValid(invoicer.Siren))
            {
                // « identifié » = SIREN BT-30 présent et valide (clé de Luhn) — règle de rôle générale (F15 §1.8),
                // comme l'identité émetteur/acheteur (VAL02). Un SIRET seul ne suffit pas (acceptance RD404).
                var reason = string.IsNullOrWhiteSpace(invoicer.Siren)
                    ? "n'a pas de SIREN renseigné"
                    : $"a un SIREN invalide ({invoicer.Siren}, clé de Luhn)";
                issues.Add(ValidationIssue.Blocking(
                    SelfBilledInvoicerUnidentified,
                    $"Le document n° {document.Number} est marqué auto-facturé (389) mais l'émetteur matériel (« {invoicer.Name} ») {reason}. Renseignez le SIREN du mandataire émetteur dans le logiciel source ; le document reste bloqué tant que l'émetteur matériel n'est pas identifié.",
                    $"IsSelfBilled=true ; Invoicer.Siren='{invoicer.Siren}' n'est pas un SIREN valide (F15 §1.8, F04 §4.1).",
                    "BT-30"));
            }
        }
        else if (invoicer is not null)
        {
            // Réciproque : un émetteur distinct du vendeur sans indicateur d'auto-facturation est incohérent
            // (ADR-0004 D3-6 : Invoicer = auto-facturation / facturation pour compte de tiers). Bloquer plutôt
            // que projeter un type de document erroné (380 au lieu de 389).
            issues.Add(ValidationIssue.Blocking(
                InvoicerWithoutSelfBilled,
                $"Le document n° {document.Number} porte un émetteur matériel distinct du vendeur (« {invoicer.Name} ») mais n'est PAS marqué auto-facturé. Un émetteur de facture différent du vendeur relève de l'auto-facturation (art. 289 I-2 du CGI, type 389) : marquez le document comme auto-facturé dans le logiciel source, ou retirez l'émetteur matériel s'il est identique au vendeur. Le document reste bloqué.",
                "Invoicer présent mais IsSelfBilled=false (incohérence de rôle ; ADR-0004 D3-6).",
                "BT-3"));
        }
    }

    private static void AddPayeeDeferralWarning(PivotDocumentDto document, List<ValidationIssue> issues)
    {
        // Payee (BG-10, affacturage) est au contrat + au hash canonique mais INERTE : aucun sérialiseur PA ne
        // le projette en V1 (RD4-09). Décision RD404 = DIFFÉRÉ EXPLICITE (RD409, addendum ADR-0004-bis). On ne
        // bloque pas (donnée optionnelle, pas un faux) mais on SIGNALE pour ne pas le laisser passer pour transmis.
        if (document.Payee is null)
        {
            return;
        }

        issues.Add(ValidationIssue.Warning(
            PayeeNotTransmitted,
            $"Le document n° {document.Number} désigne un bénéficiaire de paiement distinct du vendeur (« {document.Payee.Name} », affacturage) qui n'est PAS transmis à la Plateforme Agréée dans cette version. Vérifiez que c'est acceptable, ou traitez l'affacturage hors passerelle ; le document est transmis sans le bénéficiaire de paiement.",
            "Payee présent mais non projeté par les sérialiseurs PA (différé explicite RD409, addendum ADR-0004-bis).",
            "BG-10"));
    }
}
