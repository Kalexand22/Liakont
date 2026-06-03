namespace Stratum.Modules.Notification.Domain.Entities;

public sealed class WebhookSubscription
{
    private const int MinSecretLength = 32;

    private WebhookSubscription()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string EventType { get; private set; } = string.Empty;

    public string TargetUrl { get; private set; } = string.Empty;

    public string Secret { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public Guid CompanyId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public static WebhookSubscription Create(
        string name,
        string eventType,
        string targetUrl,
        string secret,
        Guid companyId)
    {
        ValidateName(name);
        ValidateEventType(eventType);
        ValidateTargetUrl(targetUrl);
        ValidateSecret(secret);

        return new WebhookSubscription
        {
            Id = Guid.NewGuid(),
            Name = name,
            EventType = eventType,
            TargetUrl = targetUrl,
            Secret = secret,
            IsActive = true,
            CompanyId = companyId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public static WebhookSubscription Reconstitute(
        Guid id,
        string name,
        string eventType,
        string targetUrl,
        string secret,
        bool isActive,
        Guid companyId,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt)
    {
        return new WebhookSubscription
        {
            Id = id,
            Name = name,
            EventType = eventType,
            TargetUrl = targetUrl,
            Secret = secret,
            IsActive = isActive,
            CompanyId = companyId,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };
    }

    public void Update(string name, string eventType, string targetUrl, string secret, bool isActive)
    {
        ValidateName(name);
        ValidateEventType(eventType);
        ValidateTargetUrl(targetUrl);
        ValidateSecret(secret);

        Name = name;
        EventType = eventType;
        TargetUrl = targetUrl;
        Secret = secret;
        IsActive = isActive;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("INV-WH-004: Name must not be empty.", nameof(name));
        }
    }

    private static void ValidateEventType(string eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new ArgumentException("INV-WH-003: Event type must not be empty.", nameof(eventType));
        }
    }

    private static void ValidateTargetUrl(string targetUrl)
    {
        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            throw new ArgumentException("INV-WH-001: Target URL must not be empty.", nameof(targetUrl));
        }

        if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("INV-WH-001: Target URL must be a valid HTTPS URL.", nameof(targetUrl));
        }
    }

    private static void ValidateSecret(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < MinSecretLength)
        {
            throw new ArgumentException(
                $"INV-WH-002: Secret must be at least {MinSecretLength} characters.", nameof(secret));
        }
    }
}
