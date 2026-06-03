namespace Stratum.Common.UI.Models;

/// <summary>
/// Fully resolved FK navigation target raised alongside a right-click on a cell
/// that carries an <see cref="EntityReference"/>. The grid pre-resolves
/// the id via <see cref="EntityReference.IdProperty"/> so the consumer only has
/// to build the URL.
/// </summary>
public sealed record EntityReferenceTarget(
    string Module,
    string Entity,
    Guid Id,
    string? RouteTemplate)
{
    /// <summary>
    /// Builds the relative URL of the referenced entity's detail page.
    /// Uses <see cref="RouteTemplate"/> when set, otherwise falls back to
    /// <c>/{module}/{entity}/{id}</c>.
    /// </summary>
    public string BuildUrl()
    {
        var template = RouteTemplate;
        if (!string.IsNullOrEmpty(template))
        {
            return template.Replace("{id}", Id.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        return $"/{Module}/{Entity}/{Id}";
    }
}
