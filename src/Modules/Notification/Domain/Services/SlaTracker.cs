namespace Stratum.Modules.Notification.Domain.Services;

using Stratum.Modules.Notification.Domain.Entities;

public static class SlaTracker
{
    public static bool CheckBreach(DeliveryRecord record, DeliverySla? sla, DateTimeOffset? asOf = null)
    {
        if (sla is null || record.SlaBreached || record.DeliveredAt is not null)
        {
            return false;
        }

        var now = asOf ?? DateTimeOffset.UtcNow;
        var elapsed = now - record.SentAt;
        return elapsed.TotalSeconds > sla.MaxDelaySeconds;
    }

    public static IReadOnlyList<DeliveryRecord> FindBreachedRecords(
        IEnumerable<DeliveryRecord> records,
        DeliverySla? sla,
        DateTimeOffset? asOf = null)
    {
        if (sla is null)
        {
            return [];
        }

        var now = asOf ?? DateTimeOffset.UtcNow;
        var threshold = now.AddSeconds(-sla.MaxDelaySeconds);

        return records
            .Where(r => r.DeliveredAt is null && !r.SlaBreached && r.SentAt < threshold)
            .ToList();
    }
}
