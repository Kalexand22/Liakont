namespace Liakont.Modules.Pipeline.Web;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Contracts.Jobs;
using Liakont.Modules.Pipeline.Contracts.Queries;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Audit;
using Stratum.Common.Abstractions.Security;
using Stratum.Modules.Job.Contracts;

/// <summary>
/// Endpoints de lecture du module Pipeline pour la console (API01b), montés sous <c>/api/v1</c> par le Host :
/// <c>GET /runs</c> (journal des traitements) et <c>GET /payments</c> (agrégats jour×taux de l'e-reporting de
/// paiement). Les deux lisent des tables possédées par le module Pipeline (<c>pipeline.run_logs</c> et la
/// projection <c>pipeline.payment_aggregations</c> de PIP03a) — d'où leur appartenance à ce projet Web.
/// Toutes les lectures sont TENANT-SCOPÉES par construction (la connexion EST le tenant — database-per-tenant,
/// blueprint §7 ; CLAUDE.md n°9/17) et exigent la permission <c>liakont.read</c>. Aucune logique métier ni
/// fiscale ici : la qualification fiscale des agrégats est calculée par PIP03a et seulement EXPOSÉE (CLAUDE.md n°2).
/// </summary>
public static class PipelineEndpointMapping
{
    /// <summary>
    /// Permission de consultation (canonique : <c>LiakontPermissions.Read</c> dans le Host, cataloguée par
    /// Identity). Référencée en chaîne car un projet de module ne référence pas le Host (frontière de dépendance).
    /// </summary>
    private const string ReadPermission = "liakont.read";

    /// <summary>
    /// Permission d'action opérateur (canonique : <c>LiakontPermissions.Actions</c> dans le Host, cataloguée par
    /// Identity). Référencée en chaîne car un projet de module ne référence pas le Host (frontière de dépendance).
    /// </summary>
    private const string ActionsPermission = "liakont.actions";

    /// <summary>Borne haute par défaut du journal des traitements (l'implémentation borne aussi à son maximum).</summary>
    private const int DefaultRunLimit = 200;

    /// <summary>
    /// Statut « décision fiscale en attente » persisté par NOM (miroir de <c>PaymentAggregationStatus.Suspended</c>,
    /// Domain — inaccessible depuis ce projet Web qui ne référence que les Contracts). Sert UNIQUEMENT à résumer en
    /// présentation « une décision fiscale est-elle en attente ? » ; la décision elle-même est celle de PIP03a.
    /// </summary>
    private const string SuspendedStatus = "Suspended";

    public static IEndpointRouteBuilder MapPipelineEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/v1/runs — journal des traitements du tenant, filtrable par intervalle de jours (from/to).
        app.MapGet("/runs", async (
            DateOnly? from,
            DateOnly? to,
            IPipelineRunQueries queries,
            CancellationToken ct) =>
        {
            var runs = await queries.GetRunsAsync(from, to, DefaultRunLimit, ct);
            return Results.Ok(runs);
        }).RequireAuthorization(ReadPermission);

        // GET /api/v1/payments — agrégats jour×taux de l'e-reporting de paiement (projection PIP03a) du tenant,
        // filtrables par période année-mois, avec l'état des paramètres fiscaux (décision en attente le cas échéant).
        app.MapGet("/payments", async (
            string? period,
            IPaymentAggregationQueries queries,
            CancellationToken ct) =>
        {
            if (!string.IsNullOrWhiteSpace(period) && !MonthPeriod.TryParse(period, out _, out _))
            {
                return Results.BadRequest("Période invalide : format attendu « AAAA-MM » (ex. 2026-01).");
            }

            var aggregates = await queries.GetAggregationsAsync(period, ct);

            // L'état « décision fiscale en attente » est EXPOSÉ depuis la qualification déjà calculée par PIP03a
            // (statut Suspended + motif opérateur), jamais redérivé d'une règle fiscale ici (CLAUDE.md n°2).
            var suspended = aggregates.FirstOrDefault(a => string.Equals(a.Status, SuspendedStatus, StringComparison.Ordinal));

            var response = new PaymentsResponse
            {
                Aggregates = aggregates,
                FiscalDecisionPending = suspended is not null,
                FiscalDecisionReason = suspended?.Reason,
            };

            return Results.Ok(response);
        }).RequireAuthorization(ReadPermission);

        // POST /api/v1/runs/trigger — déclenchement MANUEL du traitement du tenant COURANT (action de console).
        //   ?dryRun=true → simulation (« tout sauf écritures PA ») portée par la charge utile du déclencheur.
        // ACTION (permission liakont.actions). Comme « send » (ADR-0016), c'est une PUBLICATION du déclencheur
        // MONO-TENANT SendTenantTrigger sur la queue SYSTÈME (jamais la base du tenant — job orphelin), avec le
        // tenant de l'opérateur dans la charge utile : le handler rétablit ce SEUL tenant via
        // ITenantScopeFactory.Create et exécute le SEND — JAMAIS un fan-out tous-tenants (réservé au cron).
        app.MapPost("/runs/trigger", async (
            bool? dryRun,
            IServiceScopeFactory scopeFactory,
            IActorContextAccessor actorAccessor,
            IActivityLogger activityLogger,
            CancellationToken ct) =>
        {
            var actor = actorAccessor.Current;
            if (string.IsNullOrWhiteSpace(actor.TenantId))
            {
                return Results.BadRequest("Tenant non résolu : déclenchement impossible.");
            }

            var simulate = dryRun == true;

            await using var systemScope = scopeFactory.CreateAsyncScope();
            var queue = systemScope.ServiceProvider.GetRequiredService<IJobQueue>();
            var jobId = await queue.EnqueueAsync(new SendTenantTrigger(actor.TenantId, simulate), companyId: actor.CompanyId, ct: ct);

            await activityLogger.LogActivityAsync(
                "PipelineRun",
                "manual-trigger",
                "pipeline.run_triggered",
                string.Create(CultureInfo.InvariantCulture, $"Traitement déclenché manuellement par l'opérateur{(simulate ? " (simulation)" : string.Empty)}."),
                ActorId(actor),
                metadata: new { jobId, dryRun = simulate },
                companyId: actor.CompanyId,
                cancellationToken: ct);

            return Results.Accepted("/api/v1/runs", new RunTriggeredResponse(jobId, simulate));
        }).RequireAuthorization(ActionsPermission);

        return app;
    }

    /// <summary>Identité d'audit de l'opérateur (GUID utilisateur ; « system » si non authentifié, théorique ici).</summary>
    private static string ActorId(IActorContext actor) =>
        actor.IsAuthenticated ? actor.UserId.ToString() : "system";

    /// <summary>
    /// Réponse de <c>GET /payments</c> (API01b) : les agrégats jour×taux du tenant et un résumé de l'état des
    /// paramètres fiscaux — <see cref="FiscalDecisionPending"/> est vrai dès qu'au moins un agrégat est suspendu
    /// faute de décision fiscale, avec son message opérateur dans <see cref="FiscalDecisionReason"/> (WEB06 §2).
    /// </summary>
    public sealed record PaymentsResponse
    {
        /// <summary>Agrégats jour×taux du tenant (qualification fiscale portée par <c>Status</c>/<c>Reason</c>).</summary>
        public required IReadOnlyList<PaymentDailyAggregateDto> Aggregates { get; init; }

        /// <summary>Vrai si au moins un agrégat est suspendu faute de décision fiscale (décision de l'expert-comptable en attente).</summary>
        public required bool FiscalDecisionPending { get; init; }

        /// <summary>Message opérateur de la décision fiscale en attente (motif du premier agrégat suspendu), ou <c>null</c>.</summary>
        public string? FiscalDecisionReason { get; init; }
    }

    /// <summary>Accusé du déclenchement manuel (le traitement est asynchrone, exécuté par le pipeline du tenant).</summary>
    public sealed record RunTriggeredResponse(Guid JobId, bool DryRun);
}
