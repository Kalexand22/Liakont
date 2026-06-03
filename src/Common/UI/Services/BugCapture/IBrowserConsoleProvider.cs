namespace Stratum.Common.UI.Services.BugCapture;

using Stratum.Common.Infrastructure.BugCapture;

public interface IBrowserConsoleProvider
{
    Task StartAsync();

    Task StopAsync();

    Task<IReadOnlyList<BrowserConsoleEntry>> GetSnapshotAsync();
}
