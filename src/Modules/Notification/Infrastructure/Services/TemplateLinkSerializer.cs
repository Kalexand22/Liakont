namespace Stratum.Modules.Notification.Infrastructure.Services;

using System.Text.Json;
using Stratum.Modules.Notification.Domain.ValueObjects;

internal static class TemplateLinkSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Serialize(IReadOnlyList<TemplateLink> links)
    {
        if (links.Count == 0)
        {
            return "[]";
        }

        var dtos = links.Select(l => new LinkDto { Label = l.Label, UrlTemplate = l.UrlTemplate }).ToList();
        return JsonSerializer.Serialize(dtos, JsonOptions);
    }

    public static List<TemplateLink> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
        {
            return [];
        }

        var dtos = JsonSerializer.Deserialize<List<LinkDto>>(json, JsonOptions) ?? [];
        return dtos.Select(d => TemplateLink.Reconstitute(d.Label, d.UrlTemplate)).ToList();
    }

    private sealed class LinkDto
    {
        public string Label { get; set; } = string.Empty;

        public string UrlTemplate { get; set; } = string.Empty;
    }
}
