namespace Liakont.Modules.Ged.Domain.Index;

using System;
using System.Collections.Generic;
using System.Linq;
using Liakont.Modules.Ged.Domain.Catalog;

/// <summary>
/// Lien d'axe à APPENDER dans <c>ged_index.document_axis_links</c> (F19 §3.4.3/§3.7) : une valeur TYPÉE d'axe
/// portée par un document géré. GED04 n'écrit que des liens de VALEUR normale (<c>is_retraction=false</c>) ; la
/// rétractation append-only (RL-24) et le chaînage <c>supersedes_id</c> relèvent de l'UoW (garde de concurrence
/// mono-valeur, RL-02) et d'items ultérieurs. Ni <c>id</c> ni <c>seq</c> ne sont portés ici : ils sont assignés
/// par la base (<c>DEFAULT gen_random_uuid()</c> / <c>GENERATED ALWAYS AS IDENTITY</c>).
/// </summary>
public sealed class DocumentAxisLink
{
    /// <summary>Sources autorisées, miroir Domain de <c>ck_dal_source</c>.</summary>
    public static readonly IReadOnlyList<string> AllowedSources = ["agent", "manual", "ai", "import", "ocr"];

    public DocumentAxisLink(
        Guid managedDocumentId,
        Guid axisId,
        NormalizedAxisValue value,
        string source,
        decimal? confidenceScore = null,
        string? operatorIdentity = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        if (!AllowedSources.Contains(source, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"Source de valeur d'axe GED « {source} » invalide : attendu l'une de "
                    + $"[{string.Join(", ", AllowedSources)}] (miroir ck_dal_source, jamais deviner CLAUDE.md n.2).",
                nameof(source));
        }

        if (confidenceScore is < 0m or > 1m)
        {
            throw new ArgumentException(
                $"Score de confiance d'axe GED ({confidenceScore}) hors de l'intervalle [0..1] "
                    + "(miroir ck_dal_confidence).",
                nameof(confidenceScore));
        }

        ManagedDocumentId = managedDocumentId;
        AxisId = axisId;
        Value = value;
        Source = source;
        ConfidenceScore = confidenceScore;
        OperatorIdentity = operatorIdentity;
    }

    /// <summary>Document géré porteur de la valeur (<c>managed_document_id</c>, soft-link).</summary>
    public Guid ManagedDocumentId { get; }

    /// <summary>Axe du catalogue auquel la valeur se rattache (<c>axis_id</c>, soft-link).</summary>
    public Guid AxisId { get; }

    /// <summary>Valeur normalisée rangée dans SA colonne typée (anti-EAV, INV-GED-01).</summary>
    public NormalizedAxisValue Value { get; }

    /// <summary>Provenance de la valeur (<c>agent|manual|ai|import|ocr</c>).</summary>
    public string Source { get; }

    /// <summary>Score de confiance [0..1] ; <see langword="null"/> si déterministe.</summary>
    public decimal? ConfidenceScore { get; }

    /// <summary>Identité de l'opérateur (présente si <c>source='manual'</c>).</summary>
    public string? OperatorIdentity { get; }
}
