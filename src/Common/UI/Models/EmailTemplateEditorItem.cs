namespace Stratum.Common.UI.Models;

/// <summary>
/// Represents a template entry in the EmailTemplateEditor library sidebar.
/// </summary>
public sealed record EmailTemplateEditorItem
{
    public required string Code { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public string Category { get; init; } = "Workflow";

    public string Subject { get; init; } = string.Empty;

    public string BodyHtml { get; init; } = string.Empty;
}
