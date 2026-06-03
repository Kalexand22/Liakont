namespace Stratum.Common.UI.Services.BugCapture;

public interface IToastService
{
    void ShowSuccess(string message);

    void ShowError(string message);
}
