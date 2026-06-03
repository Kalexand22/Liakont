namespace Stratum.Common.UI.Services.BugCapture;

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stratum.Common.Infrastructure.BugCapture;

public sealed class ClientLogProvider : IClientLogProvider
{
    private readonly ConcurrentQueue<LogEntry> _buffer = new();
    private readonly CaptureConfiguration _config;

    public ClientLogProvider(IOptions<CaptureConfiguration> options)
    {
        _config = options.Value;
    }

    public ILogger CreateLogger(string categoryName) =>
        new CaptureLogger(categoryName, _buffer, _config.MaxLogs);

    public IReadOnlyList<LogEntry> GetSnapshot()
    {
        var all = _buffer.ToArray();
        return all.Length > _config.MaxLogs
            ? all[^_config.MaxLogs..]
            : all;
    }

    public void Dispose()
    {
    }

    private sealed class CaptureLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly ConcurrentQueue<LogEntry> _buffer;
        private readonly int _maxLogs;

        internal CaptureLogger(string categoryName, ConcurrentQueue<LogEntry> buffer, int maxLogs)
        {
            _categoryName = categoryName;
            _buffer = buffer;
            _maxLogs = maxLogs;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var entry = new LogEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                Level = logLevel.ToString(),
                Category = _categoryName,
                Message = formatter(state, exception),
                EventId = eventId.Id == 0 ? null : eventId.Id,
                Exception = exception is null ? null : BuildExceptionInfo(exception),
            };

            _buffer.Enqueue(entry);

            if (_buffer.Count > 2 * _maxLogs)
            {
                while (_buffer.Count > _maxLogs)
                {
                    _buffer.TryDequeue(out _);
                }
            }
        }

        private static ExceptionInfo BuildExceptionInfo(Exception ex) =>
            new()
            {
                Message = ex.Message,
                Type = ex.GetType().FullName ?? ex.GetType().Name,
                StackTrace = ex.StackTrace,
                InnerException = ex.InnerException is null ? null : BuildExceptionInfo(ex.InnerException),
            };
    }
}
