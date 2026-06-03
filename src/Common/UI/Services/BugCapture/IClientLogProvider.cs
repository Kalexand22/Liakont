namespace Stratum.Common.UI.Services.BugCapture;

using Microsoft.Extensions.Logging;
using Stratum.Common.Infrastructure.BugCapture;

public interface IClientLogProvider : ILoggerProvider
{
    IReadOnlyList<LogEntry> GetSnapshot();
}
