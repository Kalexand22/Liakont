namespace Liakont.Modules.Ged.Domain.Mapping;

/// <summary>
/// Résout un code d'axe vers sa définition minimale de mapping (<see cref="AxisMappingTarget"/>). Abstraction
/// PURE injectée dans <see cref="GedMapper"/> : elle permet de garder le mapper testable (catalogue en mémoire
/// dans les goldens) tout en laissant l'implémentation runtime lire <c>ged_catalog.axis_definitions</c> du
/// tenant. Un axe INCONNU ou INACTIF rend <see langword="null"/> ⇒ le mapper DÉFÈRE (jamais deviner, règle 3).
/// </summary>
public interface IAxisMappingCatalog
{
    /// <summary>Résout un axe par son code ; rend <see langword="null"/> si l'axe est inconnu ou inactif.</summary>
    /// <param name="axisCode">Le code de l'axe cible.</param>
    /// <returns>La cible de mapping, ou <see langword="null"/>.</returns>
    AxisMappingTarget? Resolve(string axisCode);
}
