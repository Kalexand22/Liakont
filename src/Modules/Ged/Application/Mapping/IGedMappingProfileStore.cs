namespace Liakont.Modules.Ged.Application.Mapping;

using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Ged.Domain.Mapping;

/// <summary>
/// Surface de LECTURE des profils de mapping GED validés (F19 §4.5), tenant-scopée par la connexion (règle 9).
/// Consommée par le consommateur d'ingestion GED (GED05b) qui, pour chaque document, charge le profil VALIDÉ de
/// son <c>documentType</c> et appelle <see cref="GedMapper.Map"/> (mappé) ou range le document en <c>deferred</c>.
/// N'expose JAMAIS un profil non validé (un profil non validé n'est pas appliqué — comme <c>MappingTable</c>).
/// </summary>
public interface IGedMappingProfileStore
{
    /// <summary>
    /// Charge le profil VALIDÉ du <paramref name="documentType"/> pour le tenant courant, ou <see langword="null"/>
    /// si aucun profil validé n'existe (⇒ l'appelant défère le document).
    /// </summary>
    /// <param name="documentType">Le type de document source.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <returns>Le profil validé, ou <see langword="null"/>.</returns>
    Task<GedMappingProfile?> GetValidatedProfileAsync(string documentType, CancellationToken cancellationToken = default);
}
