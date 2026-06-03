namespace Stratum.Common.UI.Components;

using Microsoft.AspNetCore.Components;
using Stratum.Common.UI.Models;

public partial class EmailTemplateEditor
{
    private readonly string _uid = Guid.NewGuid().ToString("N")[..8];

    private string _selectedCode = string.Empty;
    private string _name = string.Empty;
    private string _subject = string.Empty;
    private string _category = string.Empty;
    private string? _bodyHtml;
    private PreviewMode _previewMode = PreviewMode.Desktop;
    private DateTimeOffset? _lastModified;

    private enum PreviewMode
    {
        Desktop,
        Mobile,
    }

    /// <summary>Available templates shown in the left sidebar library.</summary>
    [Parameter]
    public IReadOnlyList<EmailTemplateEditorItem>? Templates { get; set; }

    /// <summary>Available dynamic variables for insertion.</summary>
    [Parameter]
    public IReadOnlyList<EmailTemplateVariable>? Variables { get; set; }

    /// <summary>Available category options for the dropdown.</summary>
    [Parameter]
    public IReadOnlyList<string> Categories { get; set; } = ["Workflow", "SLA", "Manuel"];

    /// <summary>Raised when the user clicks "Publier le modele".</summary>
    [Parameter]
    public EventCallback<EmailTemplateEditorSaveArgs> OnPublish { get; set; }

    /// <summary>Raised when the user clicks "Enregistrer en tant que version...".</summary>
    [Parameter]
    public EventCallback<EmailTemplateEditorSaveArgs> OnSaveVersion { get; set; }

    /// <summary>Raised when the user clicks "Envoyer un test".</summary>
    [Parameter]
    public EventCallback<EmailTemplateEditorSaveArgs> OnSendTest { get; set; }

    /// <summary>Raised when any field changes (name, subject, category, body).</summary>
    [Parameter]
    public EventCallback<EmailTemplateEditorSaveArgs> OnChange { get; set; }

    /// <summary>Raised when a template is selected from the library.</summary>
    [Parameter]
    public EventCallback<EmailTemplateEditorItem> OnTemplateSelected { get; set; }

    /// <summary>Optional test identifier for E2E tests.</summary>
    [Parameter]
    public string? TestId { get; set; }

    /// <summary>Accessible label for the editor region.</summary>
    [Parameter]
    public string? AriaLabel { get; set; }

    private static string FormatRelativeTime(DateTimeOffset time)
    {
        var elapsed = DateTimeOffset.UtcNow - time;
        if (elapsed.TotalMinutes < 1)
        {
            return "il y a quelques secondes";
        }

        if (elapsed.TotalMinutes < 60)
        {
            return $"il y a {(int)elapsed.TotalMinutes} min";
        }

        return $"il y a {(int)elapsed.TotalHours} h";
    }

    private async Task SelectTemplate(EmailTemplateEditorItem template)
    {
        _selectedCode = template.Code;
        _name = template.Name;
        _subject = template.Subject;
        _category = template.Category;
        _bodyHtml = template.BodyHtml;
        _lastModified = DateTimeOffset.UtcNow;

        await OnTemplateSelected.InvokeAsync(template);
        await NotifyChangeAsync();
    }

    private async Task InsertVariable(EmailTemplateVariable variable)
    {
        var placeholder = $"{{{{{variable.Name}}}}}";
        _bodyHtml = (_bodyHtml ?? string.Empty) + placeholder;
        _lastModified = DateTimeOffset.UtcNow;
        await NotifyChangeAsync();
    }

    private async Task OnNameInput(ChangeEventArgs e)
    {
        _name = e.Value?.ToString() ?? string.Empty;
        _lastModified = DateTimeOffset.UtcNow;
        await NotifyChangeAsync();
    }

    private async Task OnSubjectInput(ChangeEventArgs e)
    {
        _subject = e.Value?.ToString() ?? string.Empty;
        _lastModified = DateTimeOffset.UtcNow;
        await NotifyChangeAsync();
    }

    private async Task OnCategoryChange(ChangeEventArgs e)
    {
        _category = e.Value?.ToString() ?? string.Empty;
        _lastModified = DateTimeOffset.UtcNow;
        await NotifyChangeAsync();
    }

    private async Task HandlePublish()
    {
        await OnPublish.InvokeAsync(BuildArgs());
    }

    private async Task HandleSaveVersion()
    {
        await OnSaveVersion.InvokeAsync(BuildArgs());
    }

    private async Task HandleSendTest()
    {
        await OnSendTest.InvokeAsync(BuildArgs());
    }

    private async Task NotifyChangeAsync()
    {
        await OnChange.InvokeAsync(BuildArgs());
    }

    private EmailTemplateEditorSaveArgs BuildArgs() => new()
    {
        Code = _selectedCode,
        Name = _name,
        Subject = _subject,
        Category = _category,
        BodyHtml = _bodyHtml ?? string.Empty,
    };

    private string ResolveVariables(string template)
    {
        if (string.IsNullOrEmpty(template) || Variables is null)
        {
            return template;
        }

        var result = template;
        foreach (var v in Variables)
        {
            var placeholder = $"{{{{{v.Name}}}}}";
            var value = v.SampleValue ?? v.Name;
            result = result.Replace(placeholder, value);
        }

        return result;
    }
}
