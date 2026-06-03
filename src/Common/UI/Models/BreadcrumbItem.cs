namespace Stratum.Common.UI.Models;

/// <summary>A single item in a breadcrumb navigation trail.</summary>
/// <param name="Label">Display text.</param>
/// <param name="Href">Navigation URL. Null for the current (last) item.</param>
public sealed record BreadcrumbItem(string Label, string? Href = null);
