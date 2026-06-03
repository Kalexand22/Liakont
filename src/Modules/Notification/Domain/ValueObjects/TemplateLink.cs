namespace Stratum.Modules.Notification.Domain.ValueObjects;

public sealed record TemplateLink
{
    private TemplateLink()
    {
    }

    public string Label { get; private init; } = string.Empty;

    public string UrlTemplate { get; private init; } = string.Empty;

    public static TemplateLink Create(string label, string urlTemplate)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("INV-NOTIF-020: Template link label must not be empty.", nameof(label));
        }

        if (string.IsNullOrWhiteSpace(urlTemplate))
        {
            throw new ArgumentException("INV-NOTIF-021: Template link URL template must not be empty.", nameof(urlTemplate));
        }

        return new TemplateLink { Label = label.Trim(), UrlTemplate = urlTemplate.Trim() };
    }

    public static TemplateLink Reconstitute(string label, string urlTemplate)
    {
        return new TemplateLink { Label = label, UrlTemplate = urlTemplate };
    }
}
