namespace Stratum.Common.UI.Services;

using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.FieldChange;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.Abstractions.UiRules;

/// <summary>
/// Bridges Blazor form field changes to <see cref="IFieldChangeEngine"/> with
/// debounce support (300 ms or flush-on-blur). Applies <c>FieldsToSet</c> back
/// to the entity via reflection and exposes the merged <see cref="UiAttributeSet"/>.
/// <para>
/// <strong>Thread affinity:</strong> This class is designed for Blazor Server components
/// and relies on the single-threaded synchronization context of a Blazor circuit.
/// Do not call <see cref="NotifyFieldChanged"/> or <see cref="FlushAsync"/> from
/// background threads or SignalR hub callbacks outside the circuit scope.
/// </para>
/// </summary>
public sealed partial class FieldChangeMediator<TEntity> : IAsyncDisposable
    where TEntity : class
{
    private readonly IFieldChangeEngine _engine;
    private readonly IActorContextAccessor _actorAccessor;
    private readonly ILogger<FieldChangeMediator<TEntity>> _logger;
    private readonly HashSet<string> _pendingFields = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    private TEntity? _entity;
    private Func<Task>? _stateHasChanged;
    private CancellationTokenSource? _debounceCts;

    /// <summary>
    /// Initializes a new instance of the <see cref="FieldChangeMediator{TEntity}"/> class.
    /// </summary>
    public FieldChangeMediator(
        IFieldChangeEngine engine,
        IActorContextAccessor actorAccessor,
        ILogger<FieldChangeMediator<TEntity>> logger)
    {
        _engine = engine;
        _actorAccessor = actorAccessor;
        _logger = logger;
    }

    /// <summary>Gets or sets the debounce interval in milliseconds. Default: 300.</summary>
    public int DebounceMs { get; set; } = 300;

    /// <summary>
    /// Gets the current merged UI attributes produced by the last field-change processing.
    /// Components should read this to apply hidden/readonly/required state.
    /// </summary>
    public UiAttributeSet CurrentUiAttributes { get; private set; } = new();

    /// <summary>
    /// Gets the last <see cref="FieldChangeResult"/> returned by the engine.
    /// Useful for inspecting which fields were set.
    /// </summary>
    public FieldChangeResult? LastResult { get; private set; }

    /// <summary>
    /// Gets or sets an optional callback invoked after changes are processed, before StateHasChanged.
    /// </summary>
    public Func<FieldChangeResult, Task>? OnChangesProcessed { get; set; }

    /// <summary>
    /// Gets or sets an optional callback invoked when the engine or a handler throws.
    /// Components can use this to display a toast or error message.
    /// </summary>
    public Action<Exception>? OnError { get; set; }

    /// <summary>
    /// Initializes the mediator with the entity instance and a callback to trigger
    /// Blazor re-rendering. Pass <c>() =&gt; InvokeAsync(StateHasChanged)</c>.
    /// </summary>
    public void Initialize(TEntity entity, Func<Task> stateHasChanged)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(stateHasChanged);
        _entity = entity;
        _stateHasChanged = stateHasChanged;
    }

    /// <summary>
    /// Notifies the mediator that a field has changed. The change is batched and
    /// processed after the debounce interval (or immediately via <see cref="FlushAsync"/>).
    /// </summary>
    public void NotifyFieldChanged(string fieldName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        EnsureInitialized();

        lock (_lock)
        {
            _pendingFields.Add(fieldName);
        }

        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();
        _ = ProcessAfterDebounceAsync(_debounceCts.Token);
    }

    /// <summary>
    /// Immediately processes all pending field changes without waiting for debounce.
    /// Typically called on blur events.
    /// </summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        EnsureInitialized();
        _debounceCts?.Cancel();

        HashSet<string> fields;
        lock (_lock)
        {
            if (_pendingFields.Count == 0)
            {
                return;
            }

            fields = new HashSet<string>(_pendingFields, StringComparer.Ordinal);
            _pendingFields.Clear();
        }

        await ProcessChangesAsync(fields, ct);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;
        return ValueTask.CompletedTask;
    }

    private static object? ConvertValue(object? value, Type targetType)
    {
        if (value is null)
        {
            return null;
        }

        var valueType = value.GetType();

        // No conversion needed — exact type match or assignable
        if (targetType.IsAssignableFrom(valueType))
        {
            return value;
        }

        // Enum conversion (from string or underlying numeric type)
        if (targetType.IsEnum)
        {
            return value is string s
                ? Enum.Parse(targetType, s, ignoreCase: true)
                : Enum.ToObject(targetType, value);
        }

        // Standard IConvertible path (decimal, int, double, string, DateTime, etc.)
        return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "FieldChangeMediator: error processing changes for {EntityType}")]
    private partial void LogProcessingError(Exception ex, string entityType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "FieldChangeMediator: skipping non-writable property {Property} on {EntityType}")]
    private partial void LogSkippingProperty(string property, string entityType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "FieldChangeMediator: failed to set {Property} on {EntityType}")]
    private partial void LogSetPropertyFailed(Exception ex, string property, string entityType);

    private async Task ProcessAfterDebounceAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(DebounceMs, ct);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        HashSet<string> fields;
        lock (_lock)
        {
            if (_pendingFields.Count == 0)
            {
                return;
            }

            fields = new HashSet<string>(_pendingFields, StringComparer.Ordinal);
            _pendingFields.Clear();
        }

        await ProcessChangesAsync(fields, CancellationToken.None);
    }

    private async Task ProcessChangesAsync(IReadOnlySet<string> changedFields, CancellationToken ct)
    {
        try
        {
            var result = await _engine.ProcessChangesAsync(
                _entity!,
                changedFields,
                _actorAccessor.Current,
                ct);

            LastResult = result;

            ApplyFieldsToSet(result.FieldsToSet);

            if (result.UiAttributes is not null)
            {
                CurrentUiAttributes = result.UiAttributes;
            }

            if (OnChangesProcessed is not null)
            {
                await OnChangesProcessed(result);
            }

            if (_stateHasChanged is not null)
            {
                await _stateHasChanged();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogProcessingError(ex, typeof(TEntity).Name);
            OnError?.Invoke(ex);
        }
    }

    private void ApplyFieldsToSet(IReadOnlyDictionary<string, object?> fieldsToSet)
    {
        if (fieldsToSet.Count == 0)
        {
            return;
        }

        var type = typeof(TEntity);
        foreach (var (fieldName, value) in fieldsToSet)
        {
            var prop = type.GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance);
            if (prop is null || !prop.CanWrite)
            {
                LogSkippingProperty(fieldName, type.Name);
                continue;
            }

            try
            {
                var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                var converted = ConvertValue(value, targetType);
                prop.SetValue(_entity, converted);
            }
            catch (Exception ex)
            {
                LogSetPropertyFailed(ex, fieldName, type.Name);
            }
        }
    }

    private void EnsureInitialized()
    {
        if (_entity is null || _stateHasChanged is null)
        {
            throw new InvalidOperationException(
                "FieldChangeMediator must be initialized with Initialize(entity, stateHasChanged) before use.");
        }
    }
}
