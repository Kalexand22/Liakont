namespace Stratum.Common.UI.Services.BugCapture;

/// <summary>
/// No-op toast service used when no UI toast provider is registered.
/// Replace by registering a concrete <see cref="IToastService"/> implementation before AddCommonUI.
/// </summary>
internal sealed class NullToastService : IToastService
{
    public void ShowSuccess(string message)
    {
    }

    public void ShowError(string message)
    {
    }
}
