namespace Liakont.Modules.Pipeline.Infrastructure.Check;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Mandats.Contracts;
using Liakont.Modules.Pipeline.Domain.B2cReporting;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.Transmission.Contracts;
using Liakont.Modules.TvaMapping.Contracts.Services;
using Liakont.Modules.Validation.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Cœur de décision du CHECK (PIP01b), SANS I/O de staging : à partir d'un pivot DÉJÀ relu (et son hash
/// re-vérifié par le magasin de staging) et du tenant, applique le mapping TVA (table validée), la GARDE-FOU
/// PRODUCTION puis la VALIDATION, et retourne soit un motif de blocage opérateur, soit la version de table
/// appliquée (document prêt à l'envoi).
/// </summary>
/// <remarks>
/// <para>Partagé par DEUX appelants : le consommateur CHECK (<see cref="DocumentReceivedConsumer"/>,
/// <c>Detected → ReadyToSend/Blocked</c>) ET la réconciliation des avoirs (PIP02,
/// <c>SendTenantJob.ReconcileCreditNotesAsync</c>, <c>Blocked → ReadyToSend</c> dès que la facture d'origine
/// est émise). Une SEULE source de la décision de blocage fiscal — deux implémentations divergentes seraient
/// un risque de conformité (l'une pourrait laisser passer ce que l'autre bloque, CLAUDE.md n°2/3).</para>
/// <para>AUCUNE règle fiscale inventée : le triplet {catégorie, taux, VATEX} vient de la table validée du
/// tenant (module TvaMapping) ; la part fournie au mapping est <see cref="CheckTvaMapping.LinePart"/>
/// (<c>Autre</c>). La garde-fou production n'est JAMAIS affaiblie (CLAUDE.md n°3). Détection seule, aucune
/// écriture — l'évaluateur ne fait avancer AUCUNE machine à états (c'est l'appelant qui transitionne).</para>
/// </remarks>
internal static class DocumentCheckEvaluator
{
    /// <summary>Nom sérialisé de l'environnement <c>PaEnvironment.Production</c> (exposé en chaîne par les Contracts TenantSettings).</summary>
    private const string ProductionEnvironmentName = "Production";

    /// <summary>Motif de blocage de la garde-fou production (item PIP01b §3). NE JAMAIS affaiblir (CLAUDE.md n°3, P1).</summary>
    private const string ProductionGuardReason =
        "Table de mapping TVA non validée. Un compte Plateforme Agréée actif est en environnement « Production » : " +
        "aucun document n'est transmis tant que la table TVA n'est pas validée (table d'exemple ou modifiée non " +
        "revalidée). Action opérateur : validez la table de mapping TVA (Paramétrage › TVA).";

    /// <summary>Motif de blocage quand aucune table de mapping n'existe pour le tenant.</summary>
    private const string TableAbsentReason =
        "Aucune table de mapping TVA n'est définie pour ce tenant : document bloqué (aucune catégorie n'est " +
        "devinée). Action opérateur : créez puis validez la table dans la console (Paramétrage › TVA) avant tout " +
        "envoi.";

    /// <summary>
    /// Motif de blocage quand la nature d'opération du tenant n'est pas paramétrée (ADR-0031 amendé : la nature
    /// d'opération est remplie par la plateforme à l'ingestion depuis le paramétrage fiscal — elle n'est plus
    /// portée par l'agent). Absente = bloqué, jamais devinée (CLAUDE.md n°2/n°3).
    /// </summary>
    private const string OperationCategoryMissingReason =
        "La nature d'opération (livraison de biens / prestation de services / mixte) n'est pas paramétrée pour ce " +
        "tenant : document bloqué (aucune nature n'est devinée). Action opérateur : renseignez la nature " +
        "d'opération dans la console (Paramétrage › Fiscal).";

    /// <summary>
    /// Évalue un document : mapping TVA → garde-fou production → validation. Les services (mapping,
    /// paramétrage tenant, validation) sont résolus depuis le scope tenant <paramref name="services"/>
    /// (database-per-tenant, isolation par la connexion — CLAUDE.md n°9).
    /// </summary>
    /// <param name="services">Fournisseur de services du scope TENANT courant.</param>
    /// <param name="companyId">Société du tenant (clé d'isolation du mapping et de la validation).</param>
    /// <param name="documentId">
    /// Identifiant du document — clé (avec <paramref name="companyId"/>) de la garde d'émission self-billed
    /// (MND03, ADR-0024 §3) : un document <c>IsSelfBilled</c> n'est émissible que si son acceptation est acquise.
    /// </param>
    /// <param name="documentNumber">Numéro de document, préfixé aux motifs rédigés par le CHECK (CLAUDE.md n°12).</param>
    /// <param name="pivot">Le pivot relu (hash déjà re-vérifié par le magasin de staging).</param>
    /// <param name="buyerConfirmedB2C">
    /// Verdict OPÉRATEUR « acheteur confirmé particulier (B2C) » du garde-fou B2B/B2C (F08 §A.4, item API02b) :
    /// quand <c>true</c>, l'anomalie <c>BUYER_LOOKS_PROFESSIONAL</c> (VAL05) n'est plus produite pour ce
    /// document (décision tranchée et journalisée incorporée à la validation, jamais un affaiblissement
    /// silencieux). Par défaut <c>false</c> (CHECK nominal sur un document <c>Detected</c> sans verdict).
    /// Seul le chemin de RE-VÉRIFICATION (recheck, API02b) le passe à <c>true</c> ; la garde-fou PRODUCTION
    /// (table TVA non validée) et toutes les autres règles restent appliquées sans changement.
    /// </param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <returns>La décision : bloqué (motif) ou prêt (version de table).</returns>
    public static async Task<CheckDecision> EvaluateAsync(
        IServiceProvider services,
        Guid companyId,
        Guid documentId,
        string documentNumber,
        PivotDocumentDto pivot,
        bool buyerConfirmedB2C = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(pivot);

        // Émetteur rempli au READ-TIME depuis le profil tenant COURANT (ADR-0031 amendé / RB9) : l'agent ne
        // porte plus l'émetteur, et l'anti-doublon F06 hashe le pivot SOURCE à l'ingestion. On enrichit ICI,
        // avant mapping/validation, pour que SupplierIdentityRule et la nature d'opération voient l'identité
        // du tenant. Idempotent : un émetteur déjà porté (389 = mandant) n'est pas écrasé.
        var tenantSettings = services.GetRequiredService<ITenantSettingsQueries>();
        pivot = await PivotEmitterEnricher.EnrichFromTenantAsync(pivot, tenantSettings, companyId, cancellationToken);

        var mappingService = services.GetRequiredService<ITvaMappingService>();
        var plan = CheckTvaMapping.BuildPlan(pivot);
        var mapping = await mappingService.MapAsync(companyId, plan.Requests, cancellationToken);

        if (!mapping.TableExists)
        {
            // Table absente AVANT la garde-fou production : sinon, en production (IsValidated=false), l'opérateur
            // recevrait « faites valider la table » alors qu'il n'en existe AUCUNE — l'action correcte est « créez
            // la table » (message opérateur exact, CLAUDE.md n°12). Le document reste Blocked dans les deux cas.
            // FIX06 (D5) : on AGRÈGE aussi les motifs indépendants du mapping (garde-fou B2B/B2C, forme, identité)
            // pour que l'opérateur voie dès ce premier CHECK tout ce qui est corrigeable sans la table TVA.
            return await BlockedWithIndependentIssuesAsync(
                services, companyId, documentNumber, TableAbsentReason, pivot, buyerConfirmedB2C, cancellationToken);
        }

        if (!mapping.IsValidated && await IsProductionContextAsync(tenantSettings, companyId, cancellationToken))
        {
            // GARDE-FOU PRODUCTION (item PIP01b §3) — table non validée + compte PA production = tout reste Blocked.
            // SÉQUENTIELLE (FIX06, décision D5) : on n'agrège PAS d'autres motifs ici — en production avec table non
            // validée, RIEN ne circule, et l'action unique attendue est « faites valider la table ». NE JAMAIS
            // affaiblir (CLAUDE.md n°3, P1).
            return CheckDecision.Blocked(WithDocumentNumber(documentNumber, ProductionGuardReason));
        }

        var evaluation = CheckTvaMapping.Evaluate(pivot, plan, mapping);
        if (evaluation.IsBlocked)
        {
            // Motifs de mapping/forme rédigés par CHECK (TvaMapper/CheckTvaMapping) : on cite le n° de document
            // (CLAUDE.md n°12). FIX06 (D5) : on AGRÈGE en plus les motifs indépendants du mapping — l'opérateur
            // découvre toutes les causes corrigeables d'un coup, plus une par une à coups de « Revérifier ».
            return await BlockedWithIndependentIssuesAsync(
                services, companyId, documentNumber, evaluation.BlockReason!, pivot, buyerConfirmedB2C, cancellationToken);
        }

        // GARDE FAIL-CLOSED « marge non classée » (CLAUDE.md n°3) : un document à la FORME d'une marge B2C
        // (honoraires + aucune TVA distincte) que le mapping validé NE reconnaît PAS marge (exonéré non-marge,
        // ou acheteur professionnel sous régime exonéré) ne doit JAMAIS filer par la voie document — ses
        // honoraires (hors lignes) y seraient PERDUS, marge sous-déclarée sans message opérateur. On BLOQUE,
        // symétrique au pré-filtre du job agrégé B4. Une vraie marge B2C est marquée (déférée vers B4) ; un
        // document taxable (TVA distincte) garde sa voie nominale. Le marqueur est dérivé dans CheckTvaMapping.
        if (B2cMarginMarking.LooksLikeUnclassifiedMargin(evaluation.EnrichedDocument!))
        {
            return CheckDecision.Blocked(
                WithDocumentNumber(documentNumber, BuildUnclassifiedMarginReason(evaluation.EnrichedDocument!)));
        }

        var validation = services.GetRequiredService<IValidationService>();
        var validationResult = await validation.ValidateAsync(
            new DocumentValidationContext(evaluation.EnrichedDocument!, companyId, buyerConfirmedB2C), cancellationToken);

        if (validationResult.HasBlockingIssue)
        {
            return CheckDecision.Blocked(AggregateBlockingIssues(validationResult));
        }

        // GARDE D'ÉMISSION SELF-BILLED (MND03, ADR-0024 §3 / F15 §2.3, INV-ACCEPT-2) — DERNIÈRE garde avant
        // l'émissibilité, par INVERSION DE DÉPENDANCE : un document self-billed (autofacturation sous mandat,
        // art. 289 I-2 CGI) n'est émissible que si son acceptation est acquise (gate ouvert). Sinon, il est
        // MAINTENU Blocked (nouveau MOTIF, AUCUN nouvel état d'émission — INV-ACCEPT-1), jamais Sent
        // (« bloquer plutôt qu'émettre faux », CLAUDE.md n°3). Le pipeline n'interroge QUE l'abstraction
        // ISelfBilledGate (Mandats.Contracts), jamais le module Mandats concret (frontière P1, CLAUDE.md n°14).
        // Placée APRÈS les contrôles de contenu (un document à corriger montre d'abord ses motifs de contenu)
        // et partagée par CHECK + recheck + réconciliation des avoirs : un recheck post-acceptation rouvre le gate.
        if (pivot.IsSelfBilled)
        {
            // GARDE DE CAPACITÉ 389 (MND07, F15 §1.2 / ADR-0022, INV-MANDATS / CLAUDE.md n°8) : une auto-facture
            // sous mandat n'est émissible que si la PA active déclare la capacité d'émission 389. Sinon → bloquée
            // (jamais dégradée en facture standard 380, « bloquer plutôt qu'émettre faux » — CLAUDE.md n°3).
            // Piloté par la CAPACITÉ déclarée du plug-in, jamais un if (pa is …). Le filet d'envoi (SendTenantJob)
            // garantit en dernier ressort qu'aucun 389 ne part vers une PA incapable, même sur un changement de PA.
            var incapablePaName = await ResolveCapabilityIncapablePaAsync(services, tenantSettings, companyId, static c => c.SupportsSelfBilling, cancellationToken);
            if (incapablePaName is not null)
            {
                return CheckDecision.Blocked(WithDocumentNumber(documentNumber, SelfBilledCapabilityReason(incapablePaName)));
            }

            var verdict = await services.GetRequiredService<ISelfBilledGate>()
                .EvaluateEmissionAsync(companyId, documentId, cancellationToken);
            if (!verdict.IsEmissionAllowed)
            {
                return CheckDecision.Blocked(
                    WithDocumentNumber(documentNumber, SelfBilledAcceptanceReason(verdict.AcceptanceState)));
            }
        }

        // GARDE DE TRANSMISSION (document-driven — décision Karl 2026-06-22 « jamais une capacité d'une PA
        // n'impacte le FLUX » : le flux CLASSE, les capacités ne font que GATER la transmission au bord). Un
        // document à DESTINATAIRE IDENTIFIÉ — acheteur avec SIREN, B2B ou B2G (entité adressable, F07-F08 §A.4) —
        // n'est émissible que si la PA active a un CANAL : routage PDP B2B (SupportsB2bInvoicing) OU transport du
        // Factur-X produit par la plateforme (SupportsFacturXTransmission — ex. Generique, Chorus Pro B2G). Sinon
        // il serait dégradé en e-reporting B2C anonyme par une PA B2C-only (ex. B2Brouter) → bloquer plutôt
        // qu'émettre faux (CLAUDE.md n°3). Piloté par les CAPACITÉS déclarées, jamais un if (pa is …) (CLAUDE.md
        // n°8). Le self-billed (389) a SA propre garde ci-dessus. Filet d'envoi : SendTenantJob couvre en dernier
        // ressort un changement de PA après ReadyToSend (même patron que le 389).
        if (!pivot.IsSelfBilled && !string.IsNullOrWhiteSpace(pivot.Customer?.Siren))
        {
            var incapablePaName = await ResolveCapabilityIncapablePaAsync(
                services, tenantSettings, companyId, static c => c.SupportsB2bInvoicing || c.SupportsFacturXTransmission, cancellationToken);
            if (incapablePaName is not null)
            {
                return CheckDecision.Blocked(WithDocumentNumber(documentNumber, B2bInvoicingCapabilityReason(incapablePaName)));
            }
        }

        // Nature d'opération remplie par la plateforme à l'ingestion (ADR-0031 amendé). Si le paramétrage fiscal
        // du tenant ne la porte pas, l'émetteur enrichi la laisse nulle → on bloque (jamais devinée, CLAUDE.md n°2).
        if (pivot.OperationCategory is null)
        {
            return CheckDecision.Blocked(WithDocumentNumber(documentNumber, OperationCategoryMissingReason));
        }

        return CheckDecision.Ready(evaluation.MappingVersion, evaluation.Ventilation!, pivot.OperationCategory.Value);
    }

    /// <summary>
    /// Motif de blocage d'un document à la FORME d'une marge B2C (honoraires + aucune TVA distincte) que la
    /// table validée NE classe PAS au régime de la marge (CLAUDE.md n°2/3, F03 §6 décision #1). Transmis par la
    /// voie document, ses honoraires (DONNÉE DE CALCUL, hors lignes) seraient perdus — « bloquer plutôt qu'envoyer
    /// faux ». Diagnostiquable : le message CITE les FAITS lus sur le document enrichi (code régime source par
    /// ligne + catégorie/VATEX/taux obtenus du mapping, et le profil RÉEL de l'acheteur), jamais des hypothèses
    /// génériques — l'opérateur voit l'écart sans ouvrir le code ni la base. Aucun fait n'est deviné (CLAUDE.md n°2).
    /// </summary>
    /// <param name="enriched">Le document enrichi par le mapping TVA (lignes portant catégorie/VATEX/taux).</param>
    private static string BuildUnclassifiedMarginReason(PivotDocumentDto enriched)
    {
        var lignes = new List<string>(enriched.Lines.Count);
        for (var i = 0; i < enriched.Lines.Count; i++)
        {
            var line = enriched.Lines[i];
            var regimes = line.SourceRegimeCodes.Count == 0
                ? "aucun"
                : string.Join(", ", line.SourceRegimeCodes);
            var tax = line.Taxes.Count == 1 ? line.Taxes[0] : null;
            var categorie = tax?.CategoryCode?.ToString() ?? "non résolue";
            var vatex = string.IsNullOrWhiteSpace(tax?.VatexCode) ? "aucun" : tax!.VatexCode!;
            var taux = tax?.Rate is { } rate
                ? string.Create(CultureInfo.InvariantCulture, $"{rate} %")
                : "non résolu";
            var detail = string.Create(
                CultureInfo.InvariantCulture,
                $"ligne {i + 1} (« {line.Description} ») : régime source {regimes} → catégorie {categorie}, VATEX {vatex}, taux {taux}");
            lignes.Add(detail);
        }

        var mappingObtenu = string.Join(" ; ", lignes);
        var acheteur = DescribeBuyer(enriched.Customer);
        return
            "ce bordereau d'enchères porte des honoraires (commission acheteur/vendeur) et aucune TVA distincte, " +
            "mais la table de mapping validée ne le classe PAS au régime de la marge (l'attendraient : catégorie E " +
            $"+ VATEX-EU-F/I/J par ligne). Mapping obtenu : {mappingObtenu}. Acheteur lu : {acheteur}. Transmis " +
            "tel quel, les honoraires seraient perdus (marge sous-déclarée). Document bloqué. Action opérateur : " +
            "si c'est bien une marge B2C, complétez/corrigez la table de mapping TVA (Paramétrage › TVA) pour " +
            "mapper ce ou ces régimes source vers catégorie E + VATEX-EU-F/I/J ; si l'acheteur est un " +
            "professionnel, corrigez la donnée source (le document relève alors d'une facture B2B).";
    }

    /// <summary>
    /// Décrit FACTUELLEMENT l'acheteur lu sur le document (indices professionnels réellement présents, ou
    /// « particulier » si aucun) — pour citer le FAIT dans le motif de blocage, jamais une hypothèse. Champs du
    /// contrat pivot uniquement (frontière P1) : SIREN / SIRET / n° TVA / indice société brut.
    /// </summary>
    private static string DescribeBuyer(PivotPartyDto? buyer)
    {
        if (buyer is null)
        {
            return "aucun acheteur identifié";
        }

        var indices = new List<string>();
        if (!string.IsNullOrWhiteSpace(buyer.Siren))
        {
            indices.Add($"SIREN {buyer.Siren}");
        }

        if (!string.IsNullOrWhiteSpace(buyer.Siret))
        {
            indices.Add($"SIRET {buyer.Siret}");
        }

        if (!string.IsNullOrWhiteSpace(buyer.VatNumber))
        {
            indices.Add($"n° TVA {buyer.VatNumber}");
        }

        if (buyer.IsCompanyHint)
        {
            indices.Add("indice société");
        }

        return indices.Count == 0
            ? "particulier (aucun indice professionnel : ni SIREN, ni SIRET, ni n° TVA, ni indice société)"
            : $"professionnel ({string.Join(", ", indices)})";
    }

    /// <summary>
    /// Motif de blocage d'une auto-facture sous mandat (art. 289 I-2 CGI) dont le gate d'émission est fermé
    /// (MND03, ADR-0024 §3 / F15 §2.3, INV-ACCEPT-2). N'INVENTE aucune règle fiscale (CLAUDE.md n°2) : l'exigence
    /// d'acceptation vient de F15 §2.3 / ADR-0024 ; le périmètre du Contested (avoir de correction 261) reste
    /// NON TRANCHÉ (F15 §6.5) — on ne prescrit donc pas son traitement. Cite l'état courant (transparence) et une
    /// action corrective JUSTE (CLAUDE.md n°12). ⚠️ Depuis SIG06, le gate peut se fermer alors qu'une acceptation
    /// EST enregistrée (Accepted/TacitlyAccepted) — quand le tenant exige un niveau de preuve supérieur (eIDAS) que
    /// la validation attachée ne couvre pas : on ne doit JAMAIS affirmer « aucune acceptation enregistrée » dans ce
    /// cas (message faux + action corrective inapplicable). Préfixé du n° de document par l'appelant.
    /// </summary>
    private static string SelfBilledAcceptanceReason(string? acceptanceState)
    {
        // Acceptation ENREGISTRÉE mais gate fermé ⇒ le blocage vient du NIVEAU DE PREUVE requis (paramétrage tenant,
        // SIG06) ou de la forme, jamais d'une absence d'acceptation. Message + action corrective adaptés (CLAUDE.md
        // n°12) : ne pas dire « aucune acceptation » (factuellement faux), orienter vers le niveau requis.
        if (acceptanceState is "Accepted" or "TacitlyAccepted")
        {
            return
                "auto-facture sous mandat (art. 289 I-2 CGI) : une acceptation est enregistrée, mais elle ne " +
                "satisfait pas le niveau de preuve requis par le paramétrage du tenant pour ce document (la " +
                "validation/signature attachée est d'un niveau inférieur à l'exigence configurée). L'émission " +
                "reste suspendue — jamais transmise tant que le niveau requis n'est pas atteint. Action opérateur : " +
                "faites valider/signer ce document au niveau de preuve requis par votre paramétrage, puis relancez " +
                "la vérification.";
        }

        var situation = acceptanceState switch
        {
            "PendingAcceptance" => "l'acceptation par le mandant est en attente",
            "Contested" => "l'acceptation par le mandant a été contestée",
            _ => "aucune acceptation par le mandant n'est enregistrée",
        };

        return
            $"auto-facture sous mandat (art. 289 I-2 CGI) : {situation}. L'émission reste suspendue tant que " +
            "l'acceptation n'est pas acquise (acceptation expresse, ou bascule tacite sous mandat écrit après le " +
            "délai de contestation) — document maintenu bloqué, jamais transmis sans acceptation. Action " +
            "opérateur : faites acter l'acceptation de cette auto-facture, puis relancez la vérification.";
    }

    /// <summary>
    /// Nom de la PA active qui NE déclare PAS la capacité requise (<paramref name="declaresCapability"/>), ou
    /// <c>null</c> si la capacité est confirmée présente, si aucune PA active n'est résolue, ou si la capacité ne
    /// peut être confirmée ici (résolution déférée — le filet d'envoi reste fail-closed). Le comportement est
    /// piloté par la capacité déclarée du plug-in, JAMAIS par un <c>if (pa is …)</c> (CLAUDE.md n°8/16). La
    /// résolution est défensive (services optionnels) : on bloque UNIQUEMENT sur une incapacité CONFIRMÉE —
    /// jamais un faux blocage par indisponibilité technique (le <see cref="Send.SendTenantJob"/> garde
    /// l'émission en dernier ressort). Partagée par la garde 389 (<c>SupportsSelfBilling</c>) et la garde
    /// de transmission B2B (<c>SupportsB2bInvoicing</c>) — même logique, seul le prédicat de capacité diffère.
    /// </summary>
    private static async Task<string?> ResolveCapabilityIncapablePaAsync(
        IServiceProvider services,
        ITenantSettingsQueries tenantSettings,
        Guid companyId,
        Func<PaCapabilities, bool> declaresCapability,
        CancellationToken cancellationToken)
    {
        var accounts = await tenantSettings.GetPaAccounts(companyId, cancellationToken);
        var active = accounts.FirstOrDefault(account => account.IsActive);
        if (active is null)
        {
            // Pas de PA active à ce stade : on ne bloque pas sur la capacité (la garde d'envoi s'appliquera
            // quand une PA sera active — pas d'invention d'un blocage prématuré).
            return null;
        }

        var tenantId = services.GetService<ITenantContext>()?.TenantId;
        var registry = services.GetService<IPaClientRegistry>();
        if (string.IsNullOrWhiteSpace(tenantId) || registry is null || !registry.IsRegistered(active.PluginType))
        {
            return null;
        }

        try
        {
            var client = registry.Resolve(new PaAccountDescriptor(active.PluginType, tenantId));
            return declaresCapability(client.Capabilities) ? null : client.Capabilities.PaName;
        }
        catch (Exception)
        {
            // Résolution impossible (compte mal configuré, clé PA non saisie…) : on n'invente pas un verdict de
            // capacité — la garde d'envoi (fail-closed) tranchera. Jamais une émission dégradée pour autant (le
            // précédent TenantSettingsConsoleQueries capture aussi Exception pour ce cas — INV-TENANTSETTINGS-007).
            return null;
        }
    }

    /// <summary>
    /// Motif de blocage d'une auto-facture sous mandat (389) dont la PA active ne déclare pas la capacité
    /// d'émission 389 (MND07). N'INVENTE aucune règle fiscale : le type 389 est sourcé (F15 §1.2) et le pilotage
    /// par capacité déclarée est la règle produit (CLAUDE.md n°8). Action corrective opérateur (CLAUDE.md n°12).
    /// </summary>
    private static string SelfBilledCapabilityReason(string paName) =>
        $"auto-facture sous mandat (art. 289 I-2 CGI, type 389) : la Plateforme Agréée active « {paName} » ne " +
        "déclare pas la capacité d'émission des auto-factures (389). L'émission est suspendue — le document " +
        "n'est jamais transmis dégradé en facture standard (« bloquer plutôt qu'émettre faux »). Action " +
        "opérateur : activez une Plateforme Agréée prenant en charge l'autofacturation 389 pour ce tenant.";

    /// <summary>
    /// Motif de blocage d'un document à destinataire identifié (acheteur avec SIREN — B2B ou B2G) dont la PA
    /// active n'offre AUCUN canal de transmission : ni routage PDP B2B (<c>SupportsB2bInvoicing</c>) ni transport
    /// du Factur-X (<c>SupportsFacturXTransmission</c>). N'invente aucune règle fiscale (CLAUDE.md n°2) : la
    /// classification vient du DOCUMENT (destinataire adressable, F07-F08 §A.4) et le pilotage par capacités
    /// déclarées est la règle produit (CLAUDE.md n°8). Action corrective opérateur (n°12).
    /// </summary>
    private static string B2bInvoicingCapabilityReason(string paName) =>
        $"facture à destinataire identifié (acheteur avec SIREN) : la Plateforme Agréée active « {paName} » " +
        "n'offre aucun canal pour la transmettre — ni facturation électronique B2B (PDP), ni transport du " +
        "Factur-X. L'émission est suspendue — le document n'est jamais transmis dégradé en e-reporting B2C " +
        "anonyme (« bloquer plutôt qu'émettre faux »). Action opérateur : activez une Plateforme Agréée qui " +
        "transmet la facture (facturation B2B PDP ou transport Factur-X) pour ce tenant.";

    /// <summary>
    /// Vrai si le tenant a au moins un compte Plateforme Agréée ACTIF en environnement « Production ». Le
    /// comportement est piloté par l'environnement déclaré du compte, jamais par un plug-in PA concret.
    /// </summary>
    private static async Task<bool> IsProductionContextAsync(
        ITenantSettingsQueries tenantSettings,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PaAccountDto> accounts = await tenantSettings.GetPaAccounts(companyId, cancellationToken);
        return accounts.Any(account =>
            account.IsActive
            && string.Equals(account.Environment, ProductionEnvironmentName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Agrège les messages opérateur des anomalies BLOQUANTES de la validation en un motif unique.</summary>
    private static string AggregateBlockingIssues(ValidationResult validationResult)
    {
        var messages = validationResult.Issues
            .Where(issue => issue.Severity == ValidationSeverity.Blocking)
            .Select(issue => issue.MessageOperateur);
        return string.Join(Environment.NewLine, messages);
    }

    /// <summary>
    /// Construit une décision « bloqué » qui AGRÈGE le motif de blocage du MAPPING avec les motifs des règles de
    /// validation INDÉPENDANTES du mapping (FIX06, décision D5 — périmètre minimal). Ces règles (garde-fou
    /// B2B/B2C, forme, identité, arithmétique) sont évaluées sur le pivot NON enrichi — elles n'ont pas besoin de
    /// la catégorie/VATEX — et révèlent dès le premier CHECK tout ce qui est corrigeable INDÉPENDAMMENT de la
    /// table TVA, au lieu de le découvrir couche par couche à coups de « Revérifier ». N'affaiblit RIEN
    /// (CLAUDE.md n°3) : la décision reste « bloqué » (le motif de mapping est toujours présent) — on montre
    /// seulement PLUS de motifs. Le motif de mapping est préfixé du n° de document (CLAUDE.md n°12) ; les motifs
    /// de validation le citent déjà en propre (convention ValidationIssue) — pas de double préfixe. L'ordre est
    /// déterministe : motif de mapping en tête, puis les motifs indépendants dans l'ordre des règles.
    /// </summary>
    private static async Task<CheckDecision> BlockedWithIndependentIssuesAsync(
        IServiceProvider services,
        Guid companyId,
        string documentNumber,
        string mappingReason,
        PivotDocumentDto pivot,
        bool buyerConfirmedB2C,
        CancellationToken cancellationToken)
    {
        var reasons = new List<string> { WithDocumentNumber(documentNumber, mappingReason) };

        var validation = services.GetRequiredService<IValidationService>();
        var validationResult = await validation.ValidateMappingIndependentAsync(
            new DocumentValidationContext(pivot, companyId, buyerConfirmedB2C), cancellationToken);

        reasons.AddRange(validationResult.Issues
            .Where(issue => issue.Severity == ValidationSeverity.Blocking)
            .Select(issue => issue.MessageOperateur));

        return CheckDecision.Blocked(string.Join(Environment.NewLine, reasons));
    }

    /// <summary>Préfixe un motif de blocage rédigé par CHECK avec le numéro de document (CLAUDE.md n°12).</summary>
    private static string WithDocumentNumber(string documentNumber, string reason) =>
        string.Create(CultureInfo.InvariantCulture, $"Document n° {documentNumber} : {reason}");
}
