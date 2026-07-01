namespace Liakont.Modules.Ged.Infrastructure.Consultation;

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Liakont.Modules.Ged.Contracts.Consultation;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Écrit le journal de consultation GED (<c>ged_index.consultation_log</c>) sur la base DU TENANT via
/// <see cref="IConnectionFactory"/> (F19 §6.6, ADR-0036). L'identité de l'acteur et la corrélation par défaut sont
/// résolues server-side depuis <see cref="IActorContextAccessor"/> (anti-spoof). Deux responsabilités clés :
/// <list type="number">
/// <item><description>ROBUSTESSE (§3) — selon le <see cref="ConsultationAuditMode"/> du tenant : <c>BestEffort</c>
/// (défaut) journalise l'échec en Warning sans casser la lecture ; <c>Evidential</c> est fail-closed (lève
/// <see cref="ConsultationAuditException"/>). On ne dégrade JAMAIS en silence sous régime probant (CLAUDE.md n.3).</description></item>
/// <item><description>CONFIDENTIALITÉ (§6.5, anti-oracle) — masquage SERVER-SIDE de <c>query_text</c> ET des
/// valeurs confidentielles de <c>detail</c> quand un axe/entité ciblé est confidentiel et que l'acteur n'a pas le
/// droit <c>liakont.ged.confidential</c>. La confidentialité RÉELLE est résolue depuis le catalogue
/// (<c>ged_catalog</c>) — le prédicat est MATÉRIALISÉ ici, pas seulement en prose.</description></item>
/// </list>
/// </summary>
internal sealed partial class PostgresConsultationAuditWriter : IConsultationAuditWriter
{
    /// <summary>Marqueur de rédaction — jamais la valeur confidentielle en clair (§6.5, anti-oracle).</summary>
    internal const string RedactedMarker = "[confidentiel — masqué]";

    // Axes ciblés RÉELLEMENT confidentiels (résolution catalogue tenant, jamais une hypothèse de l'appelant).
    private const string ConfidentialAxesSql = """
        SELECT code
        FROM ged_catalog.axis_definitions
        WHERE code = ANY(@Codes) AND is_confidential = true
        """;

    private const string EntityTypeConfidentialSql = """
        SELECT is_confidential
        FROM ged_catalog.entity_types
        WHERE code = @Code
        """;

    private const string InsertSql = """
        INSERT INTO ged_index.consultation_log
            (actor_id, action, managed_document_id, entity_id, query_text, result_count, detail, correlation_id)
        VALUES
            (@ActorId, @Action, @ManagedDocumentId, @EntityId, @QueryText, @ResultCount, @Detail::jsonb, @CorrelationId)
        """;

    private readonly IConnectionFactory _connectionFactory;
    private readonly IActorContextAccessor _actorContextAccessor;
    private readonly IConsultationAuditModeProvider _modeProvider;
    private readonly ILogger<PostgresConsultationAuditWriter> _logger;

    public PostgresConsultationAuditWriter(
        IConnectionFactory connectionFactory,
        IActorContextAccessor actorContextAccessor,
        IConsultationAuditModeProvider modeProvider,
        ILogger<PostgresConsultationAuditWriter> logger)
    {
        _connectionFactory = connectionFactory;
        _actorContextAccessor = actorContextAccessor;
        _modeProvider = modeProvider;
        _logger = logger;
    }

    public async Task WriteAsync(ConsultationLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // Le régime est résolu AVANT le try : c'est un fait de configuration tenant, pas une écriture faillible ;
        // le défaut (BestEffort) ne lève jamais. On sait donc, en cas d'échec d'écriture, s'il faut fail-closed.
        var mode = await _modeProvider.GetModeAsync(cancellationToken);

        try
        {
            var actor = _actorContextAccessor.Current;

            using var connection = await _connectionFactory.OpenAsync(cancellationToken);

            // La RÉSOLUTION de confidentialité PRÉCÈDE l'INSERT : si elle échoue, on n'insère RIEN (pas d'insertion
            // en clair d'une valeur potentiellement confidentielle — fail-safe, le catch tranche selon le régime).
            var (queryText, detail) = await MaskConfidentialAsync(connection, entry, cancellationToken);

            var parameters = new
            {
                ActorId = actor.UserId.ToString(),
                Action = ToDbAction(entry.Action),
                entry.ManagedDocumentId,
                entry.EntityId,
                QueryText = queryText,
                entry.ResultCount,
                Detail = detail,
                CorrelationId = entry.CorrelationId ?? actor.CorrelationId,
            };

            await connection.ExecuteAsync(new CommandDefinition(
                InsertSql,
                parameters,
                cancellationToken: cancellationToken));
        }
        catch (Exception ex) when (ex is not ConsultationAuditException and not OperationCanceledException)
        {
            if (mode == ConsultationAuditMode.Evidential)
            {
                // Régime PROBANT : la trace est une précondition de l'accès. On NE DÉGRADE PAS en silence
                // (CLAUDE.md n.3) — Error + exception que l'appelant traduit en refus de lecture (message FR).
                LogEvidentialFailure(_logger, ex, entry.Action, _actorContextAccessor.Current.UserId);

                throw new ConsultationAuditException(
                    "La trace de consultation GED n'a pas pu être enregistrée : accès refusé (régime probant). Réessayez ; si le problème persiste, contactez l'administrateur.",
                    ex);
            }

            // Régime BEST-EFFORT (défaut) : une lecture n'a pas de transaction métier à casser. La lecture réussit ;
            // l'échec de trace est journalisé en Warning pour l'observabilité (message FR, CLAUDE.md n.12).
            LogBestEffortFailure(_logger, ex, entry.Action, _actorContextAccessor.Current.UserId);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Journal de consultation GED (régime probant) : échec d'écriture de la trace pour l'action {Action} (acteur {ActorId}) — accès refusé (fail-closed, ADR-0036 §3).")]
    private static partial void LogEvidentialFailure(
        ILogger logger, Exception ex, ConsultationAction action, Guid actorId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Journal de consultation GED (best-effort) : échec d'écriture de la trace pour l'action {Action} (acteur {ActorId}) — la lecture n'est pas interrompue.")]
    private static partial void LogBestEffortFailure(
        ILogger logger, Exception ex, ConsultationAction action, Guid actorId);

    /// <summary>
    /// Masque server-side <c>query_text</c> ET les valeurs confidentielles de <c>detail</c> si l'acteur n'a pas le
    /// droit confidentiel ET qu'un axe/entité RÉELLEMENT confidentiel est ciblé (§6.5). Renvoie le <c>query_text</c>
    /// (éventuellement masqué) et le <c>detail</c> sérialisé en <c>jsonb</c> (ou <see langword="null"/>).
    /// </summary>
    private static async Task<(string? QueryText, string? DetailJson)> MaskConfidentialAsync(
        IDbConnection connection,
        ConsultationLogEntry entry,
        CancellationToken cancellationToken)
    {
        var queryText = entry.QueryText;
        var detail = entry.Detail;

        if (!entry.ActorHasConfidentialAccess)
        {
            // Codes d'axes candidats = ceux explicitement ciblés ∪ les clés des critères/facettes.
            var candidateAxisCodes = new HashSet<string>(StringComparer.Ordinal);
            if (entry.TargetedAxisCodes is not null)
            {
                candidateAxisCodes.UnionWith(entry.TargetedAxisCodes);
            }

            if (entry.Detail is not null)
            {
                candidateAxisCodes.UnionWith(entry.Detail.Keys);
            }

            var confidentialAxes = candidateAxisCodes.Count > 0
                ? await ResolveConfidentialAxesAsync(connection, candidateAxisCodes, cancellationToken)
                : new HashSet<string>(StringComparer.Ordinal);

            var entityConfidential = !string.IsNullOrWhiteSpace(entry.TargetedEntityTypeCode)
                && await IsEntityTypeConfidentialAsync(connection, entry.TargetedEntityTypeCode!, cancellationToken);

            if (confidentialAxes.Count > 0 || entityConfidential)
            {
                // Un axe/entité confidentiel est ciblé sans le droit : le query_text peut porter la valeur
                // confidentielle → masquage INTÉGRAL (jamais en clair). Le canal de fuite ne se déplace pas vers le log.
                if (queryText is not null)
                {
                    queryText = RedactedMarker;
                }

                // Masquage ciblé des valeurs de detail dont la clé est un axe confidentiel (les autres restent).
                if (entry.Detail is not null && confidentialAxes.Count > 0)
                {
                    detail = entry.Detail.ToDictionary(
                        kv => kv.Key,
                        kv => confidentialAxes.Contains(kv.Key) ? RedactedMarker : kv.Value,
                        StringComparer.Ordinal);
                }
            }
        }

        var detailJson = detail is not null ? JsonSerializer.Serialize(detail) : null;
        return (queryText, detailJson);
    }

    private static async Task<HashSet<string>> ResolveConfidentialAxesAsync(
        IDbConnection connection,
        IReadOnlyCollection<string> codes,
        CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<string>(new CommandDefinition(
            ConfidentialAxesSql,
            new { Codes = codes.ToArray() },
            cancellationToken: cancellationToken));

        return new HashSet<string>(rows, StringComparer.Ordinal);
    }

    private static async Task<bool> IsEntityTypeConfidentialAsync(
        IDbConnection connection,
        string entityTypeCode,
        CancellationToken cancellationToken)
    {
        var value = await connection.QueryFirstOrDefaultAsync<bool?>(new CommandDefinition(
            EntityTypeConfidentialSql,
            new { Code = entityTypeCode },
            cancellationToken: cancellationToken));

        // Type d'entité inconnu = rien de confidentiel à protéger (l'exploration cible toujours un type réel).
        return value ?? false;
    }

    private static string ToDbAction(ConsultationAction action) => action switch
    {
        ConsultationAction.Search => "search",
        ConsultationAction.ViewDocument => "view_document",
        ConsultationAction.ExploreEntity => "explore_entity",
        ConsultationAction.Export => "export",
        ConsultationAction.OpenArchive => "open_archive",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Action de consultation GED inconnue."),
    };
}
