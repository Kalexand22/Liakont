namespace Stratum.Common.UI.Models;

/// <summary>
/// Marks a grid column as pointing to another entity (foreign-key reference).
/// The cell context menu uses it to offer "Ouvrir la fiche" / "Ouvrir dans un
/// nouvel onglet" actions that navigate to the referenced entity's detail page.
/// </summary>
/// <param name="Module">
/// Logical module / section used by the default route template
/// (e.g. "catalog", "showcase"). Free-form — this is just a routing hint.
/// </param>
/// <param name="Entity">
/// Entity slug used by the default route template (e.g. "products", "parties").
/// </param>
/// <param name="IdProperty">
/// Property path on the grid item type that holds the referenced entity's
/// identifier. Must be a <see cref="Guid"/> (or nullable Guid). The context
/// menu does not propose FK actions when the resolved value is empty/null.
/// </param>
/// <param name="RouteTemplate">
/// Optional explicit route template with a <c>{id}</c> placeholder
/// (e.g. <c>"/showcase/products/{id}"</c>). When null, the default
/// <c>"/{module}/{entity}/{id}"</c> is used. Provide this when the target
/// module does not follow the default pattern.
/// </param>
public sealed record EntityReference(
    string Module,
    string Entity,
    string IdProperty,
    string? RouteTemplate = null);
