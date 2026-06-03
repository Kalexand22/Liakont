namespace Stratum.Modules.Notification.Domain.Entities;

using Stratum.Modules.Notification.Domain.ValueObjects;

public sealed class EmailTemplate
{
    private List<TemplateLink> _templateLinks = [];

    private EmailTemplate()
    {
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string SubjectTemplate { get; private set; } = string.Empty;

    public string BodyTemplate { get; private set; } = string.Empty;

    public string LanguageCode { get; private set; } = "en";

    public TemplateCategory Category { get; private set; } = TemplateCategory.Transactional;

    public string? EntityType { get; private set; }

    public IReadOnlyList<TemplateLink> TemplateLinks => _templateLinks;

    public Guid? CompanyId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public static EmailTemplate Create(
        string code,
        string subjectTemplate,
        string bodyTemplate,
        string languageCode,
        Guid? companyId,
        TemplateCategory category = TemplateCategory.Transactional,
        string? entityType = null,
        IReadOnlyList<TemplateLink>? templateLinks = null)
    {
        ValidateCode(code);
        ValidateSubjectTemplate(subjectTemplate);
        ValidateBodyTemplate(bodyTemplate);
        ValidateLanguageCode(languageCode);

        return new EmailTemplate
        {
            Id = Guid.NewGuid(),
            Code = code.Trim(),
            SubjectTemplate = subjectTemplate,
            BodyTemplate = bodyTemplate,
            LanguageCode = languageCode.Trim().ToLowerInvariant(),
            CompanyId = companyId,
            Category = category,
            EntityType = entityType?.Trim(),
            _templateLinks = templateLinks?.ToList() ?? [],
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public static EmailTemplate Reconstitute(
        Guid id,
        string code,
        string subjectTemplate,
        string bodyTemplate,
        string languageCode,
        Guid? companyId,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt,
        TemplateCategory category = TemplateCategory.Transactional,
        string? entityType = null,
        IReadOnlyList<TemplateLink>? templateLinks = null)
    {
        return new EmailTemplate
        {
            Id = id,
            Code = code,
            SubjectTemplate = subjectTemplate,
            BodyTemplate = bodyTemplate,
            LanguageCode = languageCode,
            CompanyId = companyId,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            Category = category,
            EntityType = entityType,
            _templateLinks = templateLinks?.ToList() ?? [],
        };
    }

    public void Update(string subjectTemplate, string bodyTemplate)
    {
        ValidateSubjectTemplate(subjectTemplate);
        ValidateBodyTemplate(bodyTemplate);

        SubjectTemplate = subjectTemplate;
        BodyTemplate = bodyTemplate;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateCategory(TemplateCategory category, string? entityType, IReadOnlyList<TemplateLink>? templateLinks)
    {
        Category = category;
        EntityType = entityType?.Trim();
        _templateLinks = templateLinks?.ToList() ?? [];
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void ValidateCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("INV-NOTIF-002: Template code must not be empty.", nameof(code));
        }
    }

    private static void ValidateSubjectTemplate(string subjectTemplate)
    {
        if (string.IsNullOrWhiteSpace(subjectTemplate))
        {
            throw new ArgumentException("INV-NOTIF-003: Subject template must not be empty.", nameof(subjectTemplate));
        }
    }

    private static void ValidateBodyTemplate(string bodyTemplate)
    {
        if (string.IsNullOrWhiteSpace(bodyTemplate))
        {
            throw new ArgumentException("INV-NOTIF-004: Body template must not be empty.", nameof(bodyTemplate));
        }
    }

    private static void ValidateLanguageCode(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode) || languageCode.Trim().Length != 2)
        {
            throw new ArgumentException("INV-NOTIF-005: Language code must be a 2-character ISO 639-1 code.", nameof(languageCode));
        }
    }
}
