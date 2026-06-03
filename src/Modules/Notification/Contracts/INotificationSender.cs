namespace Stratum.Modules.Notification.Contracts;

using Stratum.Modules.Notification.Contracts.DTOs;

public interface INotificationSender
{
    Task SendEmailAsync(
        string templateCode,
        string languageCode,
        string recipientEmail,
        IReadOnlyDictionary<string, string> placeholders,
        Guid? companyId = null,
        CancellationToken ct = default);

    Task SendRoutedNotificationsAsync(
        string entityType,
        string entityId,
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> routingData,
        string templateCode,
        string languageCode,
        IReadOnlyDictionary<string, string> placeholders,
        Guid? companyId = null,
        CancellationToken ct = default);
}
