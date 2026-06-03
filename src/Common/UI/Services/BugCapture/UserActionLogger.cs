namespace Stratum.Common.UI.Services.BugCapture;

using System.Collections.Concurrent;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.Options;
using Stratum.Common.Infrastructure.BugCapture;

public sealed class UserActionLogger : IUserActionLogger, IDisposable
{
    private readonly ConcurrentQueue<UserActionEntry> _buffer = new();
    private readonly CaptureConfiguration _config;
    private readonly NavigationManager _navigationManager;

    public UserActionLogger(IOptions<CaptureConfiguration> options, NavigationManager navigationManager)
    {
        _config = options.Value;
        _navigationManager = navigationManager;
        _navigationManager.LocationChanged += OnLocationChanged;
    }

    public void Log(UserActionEntry entry)
    {
        _buffer.Enqueue(entry);

        if (_buffer.Count > 2 * _config.MaxUserActions)
        {
            while (_buffer.Count > _config.MaxUserActions)
            {
                _buffer.TryDequeue(out _);
            }
        }
    }

    public IReadOnlyList<UserActionEntry> GetSnapshot()
    {
        var all = _buffer.ToArray();
        return all.Length > _config.MaxUserActions
            ? all[^_config.MaxUserActions..]
            : all;
    }

    public void LogNavigation(string url, string? description = null) =>
        Log(new UserActionEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            ActionType = UserActionType.Navigation,
            Description = description ?? $"Navigated to {url}",
            Url = url,
        });

    public void LogFormAction(string description, string? target = null) =>
        Log(new UserActionEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            ActionType = UserActionType.FormSave,
            Description = description,
            Target = target,
        });

    public void LogFieldChange(string fieldName, string? value = null) =>
        Log(new UserActionEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            ActionType = UserActionType.FieldChange,
            Description = $"Field '{fieldName}' changed",
            Target = fieldName,
            Value = value,
        });

    public void LogButtonClick(string buttonName, string? target = null) =>
        Log(new UserActionEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            ActionType = UserActionType.ButtonClick,
            Description = $"Button '{buttonName}' clicked",
            Target = target ?? buttonName,
        });

    public void LogError(string description, string? target = null) =>
        Log(new UserActionEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            ActionType = UserActionType.Error,
            Description = description,
            Target = target,
        });

    public void Dispose() =>
        _navigationManager.LocationChanged -= OnLocationChanged;

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) =>
        LogNavigation(e.Location);
}
