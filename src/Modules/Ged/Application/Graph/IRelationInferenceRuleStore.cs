namespace Liakont.Modules.Ged.Application.Graph;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Ged.Domain.Graph;

/// <summary>
/// Lecture des règles d'inférence/héritage ACTIVES du tenant (F19 §10, GED24), depuis
/// <c>ged_catalog.relation_inference_rules</c>. Tenant-scopé par la connexion (INV-GED-08). Une règle inactive
/// n'est jamais retournée (donc jamais appliquée).
/// </summary>
public interface IRelationInferenceRuleStore
{
    /// <summary>Retourne les règles actives (genre/mode/borne), déjà validées vers le Domain.</summary>
    Task<IReadOnlyList<RelationInferenceRule>> GetActiveRulesAsync(CancellationToken cancellationToken = default);
}
