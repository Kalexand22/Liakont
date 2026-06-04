namespace Liakont.Modules.TenantSettings.Domain.Entities;

/// <summary>
/// Planification d'extraction d'un tenant (F12-A §5). La planification EFFECTIVE est poussée
/// vers l'agent via le heartbeat (AGT03) ; la plateforme est prioritaire (décision D3).
/// </summary>
public sealed class ExtractionSchedule
{
    private ExtractionSchedule()
    {
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>Heures de déclenchement des runs d'extraction, au format <c>HH:mm</c> (24 h).</summary>
    public IReadOnlyList<string> Hours { get; private set; } = [];

    /// <summary>Rattrapage au démarrage de l'agent (F12 §2.2/§2.4).</summary>
    public bool CatchUpOnStart { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public static ExtractionSchedule Create(Guid companyId, IReadOnlyList<string> hours, bool catchUpOnStart)
    {
        var normalized = NormalizeHours(hours);

        return new ExtractionSchedule
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Hours = normalized,
            CatchUpOnStart = catchUpOnStart,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = null,
        };
    }

    public static ExtractionSchedule Reconstitute(
        Guid id,
        Guid companyId,
        IReadOnlyList<string> hours,
        bool catchUpOnStart,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt)
    {
        return new ExtractionSchedule
        {
            Id = id,
            CompanyId = companyId,
            Hours = hours,
            CatchUpOnStart = catchUpOnStart,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };
    }

    public void Update(IReadOnlyList<string> hours, bool catchUpOnStart)
    {
        Hours = NormalizeHours(hours);
        CatchUpOnStart = catchUpOnStart;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static List<string> NormalizeHours(IReadOnlyList<string> hours)
    {
        ArgumentNullException.ThrowIfNull(hours);

        var result = new List<string>(hours.Count);
        foreach (var raw in hours)
        {
            var hour = (raw ?? string.Empty).Trim();
            if (!IsValidHour(hour))
            {
                throw new ArgumentException(
                    $"INV-TENANTSETTINGS-002 : heure de planification invalide « {raw} » (format attendu HH:mm).",
                    nameof(hours));
            }

            result.Add(hour);
        }

        return result;
    }

    private static bool IsValidHour(string hour)
    {
        return TimeOnly.TryParseExact(hour, "HH:mm", out _);
    }
}
