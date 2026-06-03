namespace Stratum.Common.UI.Services.BugCapture;

using Stratum.Common.Infrastructure.BugCapture;

public interface IUserActionLogger
{
    void Log(UserActionEntry entry);

    IReadOnlyList<UserActionEntry> GetSnapshot();

    void LogNavigation(string url, string? description = null);

    void LogFormAction(string description, string? target = null);

    void LogFieldChange(string fieldName, string? value = null);

    void LogButtonClick(string buttonName, string? target = null);

    void LogError(string description, string? target = null);
}
