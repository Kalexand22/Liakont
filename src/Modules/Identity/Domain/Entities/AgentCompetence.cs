namespace Stratum.Modules.Identity.Domain.Entities;

public sealed class AgentCompetence
{
    private AgentCompetence()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Name { get; private set; } = null!;

    public string? Category { get; private set; }

    public DateOnly? ValidUntil { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static AgentCompetence Create(
        Guid userId,
        string name,
        string? category,
        DateOnly? validUntil)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new AgentCompetence
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name,
            Category = category,
            ValidUntil = validUntil,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public static AgentCompetence Reconstitute(
        Guid id,
        Guid userId,
        string name,
        string? category,
        DateOnly? validUntil,
        DateTimeOffset createdAt)
    {
        return new AgentCompetence
        {
            Id = id,
            UserId = userId,
            Name = name,
            Category = category,
            ValidUntil = validUntil,
            CreatedAt = createdAt,
        };
    }
}
