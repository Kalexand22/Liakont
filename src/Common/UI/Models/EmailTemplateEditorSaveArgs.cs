namespace Stratum.Common.UI.Models;

/// <summary>
/// Event args emitted by EmailTemplateEditor on publish, save, or send test.
/// </summary>
public sealed record EmailTemplateEditorSaveArgs
{
    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Subject { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public string BodyHtml { get; init; } = string.Empty;
}
