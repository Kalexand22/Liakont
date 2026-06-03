namespace Stratum.Modules.Notification.Domain.Entities;

using System.Security.Cryptography;
using System.Text;

public sealed class ApiKey
{
    private ApiKey()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string KeyPrefix { get; private set; } = string.Empty;

    public string KeyHash { get; private set; } = string.Empty;

    public string[] Scopes { get; private set; } = [];

    public int RateLimit { get; private set; }

    public bool IsRevoked { get; private set; }

    public Guid CompanyId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public static (ApiKey Entity, string FullKey) Create(
        string name,
        string[] scopes,
        int rateLimit,
        Guid companyId,
        DateTimeOffset? expiresAt)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("INV-AK-001: Name must not be empty.", nameof(name));
        }

        if (rateLimit <= 0)
        {
            throw new ArgumentException("INV-AK-002: Rate limit must be positive.", nameof(rateLimit));
        }

        var fullKey = GenerateKey();
        var entity = new ApiKey
        {
            Id = Guid.NewGuid(),
            Name = name,
            KeyPrefix = fullKey[..8],
            KeyHash = HashKey(fullKey),
            Scopes = scopes,
            RateLimit = rateLimit,
            IsRevoked = false,
            CompanyId = companyId,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt,
        };

        return (entity, fullKey);
    }

    public static ApiKey Reconstitute(
        Guid id,
        string name,
        string keyPrefix,
        string keyHash,
        string[] scopes,
        int rateLimit,
        bool isRevoked,
        Guid companyId,
        DateTimeOffset createdAt,
        DateTimeOffset? revokedAt,
        DateTimeOffset? expiresAt)
    {
        return new ApiKey
        {
            Id = id,
            Name = name,
            KeyPrefix = keyPrefix,
            KeyHash = keyHash,
            Scopes = scopes,
            RateLimit = rateLimit,
            IsRevoked = isRevoked,
            CompanyId = companyId,
            CreatedAt = createdAt,
            RevokedAt = revokedAt,
            ExpiresAt = expiresAt,
        };
    }

    public void Revoke()
    {
        IsRevoked = true;
        RevokedAt = DateTimeOffset.UtcNow;
    }

    private static string GenerateKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return $"sk_{Convert.ToBase64String(bytes).Replace("+", string.Empty).Replace("/", string.Empty).Replace("=", string.Empty)}";
    }

    private static string HashKey(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexStringLower(hash);
    }
}
