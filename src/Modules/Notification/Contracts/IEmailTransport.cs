namespace Stratum.Modules.Notification.Contracts;

public interface IEmailTransport
{
    Task SendAsync(string recipient, string subject, string body, CancellationToken ct = default);
}
