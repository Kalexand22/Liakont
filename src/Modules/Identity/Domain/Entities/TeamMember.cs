namespace Stratum.Modules.Identity.Domain.Entities;

public sealed class TeamMember
{
    private TeamMember()
    {
    }

    public Guid Id { get; private set; }

    public Guid TeamId { get; private set; }

    public Guid UserId { get; private set; }

    public string? Role { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    public static TeamMember Create(Guid teamId, Guid userId, string? role)
    {
        return new TeamMember
        {
            Id = Guid.NewGuid(),
            TeamId = teamId,
            UserId = userId,
            Role = role,
            JoinedAt = DateTimeOffset.UtcNow,
        };
    }

    public static TeamMember Reconstitute(
        Guid id,
        Guid teamId,
        Guid userId,
        string? role,
        DateTimeOffset joinedAt)
    {
        return new TeamMember
        {
            Id = id,
            TeamId = teamId,
            UserId = userId,
            Role = role,
            JoinedAt = joinedAt,
        };
    }
}
