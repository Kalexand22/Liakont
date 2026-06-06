namespace Liakont.Modules.Pipeline.Infrastructure.Check;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.TvaMapping.Contracts.Services;
using Liakont.Modules.Validation.Contracts;
using Microsoft.Extensions.DependencyInjection;

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
        "Table de mapping TVA non validée — validation par l'expert-comptable requise. Un compte Plateforme " +
        "Agréée actif est en environnement « Production » : aucun document n'est transmis tant que la table TVA " +
        "n'est pas validée (table d'exemple ou modifiée non revalidée). Action opérateur : faites valider la " +
        "table de mapping TVA (Paramétrage › TVA) par l'expert-comptable.";

    /// <summary>Motif de blocage quand aucune table de mapping n'existe pour le tenant.</summary>
    private const string TableAbsentReason =
        "Aucune table de mapping TVA n'est définie pour ce tenant : document bloqué (aucune catégorie n'est " +
        "devinée). Action opérateur : créez la table dans la console (Paramétrage › TVA), puis faites-la valider " +
        "par l'expert-comptable avant tout envoi.";

    /// <summary>
    /// Évalue un document : mapping TVA → garde-fou production → validation. Les services (mapping,
    /// paramétrage tenant, validation) sont résolus depuis le scope tenant <paramref name="services"/>
    /// (database-per-tenant, isolation par la connexion — CLAUDE.md n°9).
    /// </summary>
    /// <param name="services">Fournisseur de services du scope TENANT courant.</param>
    /// <param name="companyId">Société du tenant (clé d'isolation du mapping et de la validation).</param>
    /// <param name="documentNumber">Numéro de document, préfixé aux motifs rédigés par le CHECK (CLAUDE.md n°12).</param>
    /// <param name="pivot">Le pivot relu (hash déjà re-vérifié par le magasin de staging).</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <returns>La décision : bloqué (motif) ou prêt (version de table).</returns>
    public static async Task<CheckDecision> EvaluateAsync(
        IServiceProvider services,
        Guid companyId,
        string documentNumber,
        PivotDocumentDto pivot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(pivot);

        var mappingService = services.GetRequiredService<ITvaMappingService>();
        var plan = CheckTvaMapping.BuildPlan(pivot);
        var mapping = await mappingService.MapAsync(companyId, plan.Requests, cancellationToken);

        if (!mapping.TableExists)
        {
            // Table absente AVANT la garde-fou production : sinon, en production (IsValidated=false), l'opérateur
            // recevrait « faites valider la table » alors qu'il n'en existe AUCUNE — l'action correcte est « créez
            // la table » (message opérateur exact, CLAUDE.md n°12). Le document reste Blocked dans les deux cas.
            return CheckDecision.Blocked(WithDocumentNumber(documentNumber, TableAbsentReason));
        }

        var tenantSettings = services.GetRequiredService<ITenantSettingsQueries>();
        if (!mapping.IsValidated && await IsProductionContextAsync(tenantSettings, companyId, cancellationToken))
        {
            // GARDE-FOU PRODUCTION (item PIP01b §3) — table non validée + compte PA production = tout reste Blocked.
            return CheckDecision.Blocked(WithDocumentNumber(documentNumber, ProductionGuardReason));
        }

        var evaluation = CheckTvaMapping.Evaluate(pivot, plan, mapping);
        if (evaluation.IsBlocked)
        {
            // Motifs de mapping/forme rédigés par CHECK (TvaMapper/CheckTvaMapping) : on cite le n° de document
            // (CLAUDE.md n°12). Les motifs de VALIDATION, eux, le citent déjà en propre (convention ValidationIssue).
            return CheckDecision.Blocked(WithDocumentNumber(documentNumber, evaluation.BlockReason!));
        }

        var validation = services.GetRequiredService<IValidationService>();
        var validationResult = await validation.ValidateAsync(
            new DocumentValidationContext(evaluation.EnrichedDocument!, companyId), cancellationToken);

        return validationResult.HasBlockingIssue
            ? CheckDecision.Blocked(AggregateBlockingIssues(validationResult))
            : CheckDecision.Ready(evaluation.MappingVersion, evaluation.Ventilation!, pivot.OperationCategory);
    }

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

    /// <summary>Préfixe un motif de blocage rédigé par CHECK avec le numéro de document (CLAUDE.md n°12).</summary>
    private static string WithDocumentNumber(string documentNumber, string reason) =>
        string.Create(CultureInfo.InvariantCulture, $"Document n° {documentNumber} : {reason}");
}
