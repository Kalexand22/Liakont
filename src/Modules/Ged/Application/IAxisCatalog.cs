namespace Liakont.Modules.Ged.Application;

using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Ged.Domain.Catalog;

/// <summary>
/// Port de lecture du catalogue d'axes GED (F19 §3.7/§3.8). Résout un code d'axe (paramétrage tenant) vers sa
/// <see cref="AxisDefinition"/> — type technique, échelle, cardinalité, état d'activation et, pour un axe
/// <c>enum</c>, le vocabulaire déclaré. La résolution est tenant-scopée par la connexion. Un code inconnu rend
/// <see langword="null"/> : le handler refuse (jamais deviner, règle 2), il ne crée pas d'axe implicite.
/// </summary>
public interface IAxisCatalog
{
    /// <summary>Résout un axe par son code machine ; <see langword="null"/> si aucun axe ne porte ce code.</summary>
    Task<AxisDefinition?> ResolveAsync(string axisCode, CancellationToken cancellationToken = default);
}
