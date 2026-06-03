namespace Stratum.Modules.Notification.Infrastructure.Services;

using Microsoft.Extensions.Logging;
using Stratum.Modules.Notification.Contracts;

internal sealed partial class StubEmailTransport : IEmailTransport
{
    private readonly ILogger<StubEmailTransport> _logger;

    public StubEmailTransport(ILogger<StubEmailTransport> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(string recipient, string subject, string body, CancellationToken ct = default)
    {
        LogEmailSent(_logger, recipient, subject);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "STUB: Email sent to {Recipient} with subject '{Subject}'")]
    private static partial void LogEmailSent(ILogger logger, string recipient, string subject);
}
