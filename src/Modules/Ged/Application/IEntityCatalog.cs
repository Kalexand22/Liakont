namespace Liakont.Modules.Ged.Application;

using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Ged.Domain.Catalog;

/// <summary>
/// Port de lecture du catalogue de TYPES d'entité GED (F19 §3.3.2/§4.4), symétrique d'<see cref="IAxisCatalog"/>.
/// Résout un code de type d'entité (paramétrage tenant) vers sa <see cref="EntityTypeDefinition"/> — identité,
/// clé de résolution d'identité, confidentialité, état d'activation. La résolution est tenant-scopée par la connexion.
/// Un code inconnu rend <see langword="null"/> : le consommateur DÉFÈRE (jamais deviner, règle 2/n°3), il ne crée
/// pas de type d'entité implicite.
/// </summary>
public interface IEntityCatalog
{
    /// <summary>Résout un type d'entité par son code machine ; <see langword="null"/> si aucun type ne porte ce code.</summary>
    Task<EntityTypeDefinition?> ResolveAsync(string entityTypeCode, CancellationToken cancellationToken = default);
}
