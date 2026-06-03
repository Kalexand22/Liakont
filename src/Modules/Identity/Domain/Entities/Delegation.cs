namespace Stratum.Modules.Identity.Domain.Entities;

public sealed class Delegation
{
    private Delegation()
    {
    }

    public Guid Id { get; private set; }

    public Guid DelegatorId { get; private set; }

    public Guid DelegateId { get; private set; }

    public string Scope { get; private set; } = null!;

    public DateTimeOffset ValidFrom { get; private set; }

    public DateTimeOffset ValidUntil { get; private set; }

    public string? Reason { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Delegation Create(
        Guid delegatorId,
        Guid delegateId,
        string scope,
        DateTimeOffset validFrom,
        DateTimeOffset validUntil,
        string? reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        if (delegatorId == delegateId)
        {
            throw new ArgumentException("Cannot delegate to self.");
        }

        if (validUntil <= validFrom)
        {
            throw new ArgumentException("ValidUntil must be after ValidFrom.");
        }

        return new Delegation
        {
            Id = Guid.NewGuid(),
            DelegatorId = delegatorId,
            DelegateId = delegateId,
            Scope = scope,
            ValidFrom = validFrom,
            ValidUntil = validUntil,
            Reason = reason,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public static Delegation Reconstitute(
        Guid id,
        Guid delegatorId,
        Guid delegateId,
        string scope,
        DateTimeOffset validFrom,
        DateTimeOffset validUntil,
        string? reason,
        bool isActive,
        DateTimeOffset createdAt)
    {
        return new Delegation
        {
            Id = id,
            DelegatorId = delegatorId,
            DelegateId = delegateId,
            Scope = scope,
            ValidFrom = validFrom,
            ValidUntil = validUntil,
            Reason = reason,
            IsActive = isActive,
            CreatedAt = createdAt,
        };
    }

    public void Revoke()
    {
        IsActive = false;
    }
}
