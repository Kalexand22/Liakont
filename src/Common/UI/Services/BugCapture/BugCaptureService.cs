namespace Stratum.Common.UI.Services.BugCapture;

using System.Globalization;
using System.Text;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stratum.Common.Infrastructure.BugCapture;

public sealed partial class BugCaptureService : IBugCaptureService
{
    // Liakont: Whisper API upload limit — videos above it skip the narration transcription pass.
    private const int WhisperUploadLimitBytes = 25 * 1024 * 1024;

    private readonly IUserActionLogger _userActionLogger;
    private readonly IClientLogProvider _clientLogProvider;
    private readonly IHttpTrafficRecorder _httpTrafficRecorder;
    private readonly IScreenshotProvider _screenshotProvider;
    private readonly IAudioRecorder _audioRecorder;
    private readonly IScreenRecorder _screenRecorder;
    private readonly IBrowserConsoleProvider _browserConsoleProvider;
    private readonly TranscriptionService _transcriptionService;
    private readonly IBundleReporter _bundleReporter;
    private readonly VideoAnalysisService _videoAnalysisService;
    private readonly IToastService _toastService;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly CaptureConfiguration _config;
    private readonly ILogger<BugCaptureService> _logger;

    private CaptureSession? _session;
    private CaptureBundle? _preparedBundle;

    // Caches for expensive operations (survive ReturnToEditing round-trips).
    private string? _cachedTranscription;
    private VideoAnalysis? _cachedVideoAnalysis;
    private List<MediaCapture>? _cachedMedias;

    public BugCaptureService(
        IUserActionLogger userActionLogger,
        IClientLogProvider clientLogProvider,
        IHttpTrafficRecorder httpTrafficRecorder,
        IScreenshotProvider screenshotProvider,
        IAudioRecorder audioRecorder,
        IScreenRecorder screenRecorder,
        IBrowserConsoleProvider browserConsoleProvider,
        TranscriptionService transcriptionService,
        IBundleReporter bundleReporter,
        VideoAnalysisService videoAnalysisService,
        IToastService toastService,
        IStringLocalizer<SharedResources> localizer,
        IOptions<CaptureConfiguration> options,
        ILogger<BugCaptureService> logger)
    {
        _userActionLogger = userActionLogger;
        _clientLogProvider = clientLogProvider;
        _httpTrafficRecorder = httpTrafficRecorder;
        _screenshotProvider = screenshotProvider;
        _audioRecorder = audioRecorder;
        _screenRecorder = screenRecorder;
        _browserConsoleProvider = browserConsoleProvider;
        _transcriptionService = transcriptionService;
        _bundleReporter = bundleReporter;
        _videoAnalysisService = videoAnalysisService;
        _toastService = toastService;
        _localizer = localizer;
        _config = options.Value;
        _logger = logger;
    }

    public event EventHandler? StateChanged;

    public CaptureState State { get; private set; } = CaptureState.Idle;

    public CaptureBundle? PreparedBundle => _preparedBundle;

    public ICaptureSession StartSession(CaptureType type)
    {
        _session = new CaptureSession(type, _screenshotProvider, _audioRecorder, _screenRecorder);
        ClearCaches();
        SetState(CaptureState.Capturing);

        // Start console capture (fire-and-forget, best-effort).
        _ = _browserConsoleProvider.StartAsync();

        return _session;
    }

    public async Task PrepareAsync(string comment, IReadOnlyList<string> tags)
    {
        if (_session is null)
        {
            return;
        }

        SetState(CaptureState.Finalizing);

        try
        {
            var session = _session;

            var userActions = _userActionLogger.GetSnapshot();
            var logs = _clientLogProvider.GetSnapshot()
                .Where(l => l.Timestamp >= session.StartedAt)
                .ToList();
            var httpTraffic = _httpTrafficRecorder.GetSnapshot(session.StartedAt);

            // Browser console logs.
            await _browserConsoleProvider.StopAsync();
            var browserConsoleLogs = await _browserConsoleProvider.GetSnapshotAsync();

            // Use cached results if available (from a previous prepare after ReturnToEditing).
            if (_cachedMedias is null)
            {
                await BuildExpensiveDataAsync(session);
            }

            var medias = _cachedMedias!;
            var transcription = _cachedTranscription ?? string.Empty;
            var videoAnalysis = _cachedVideoAnalysis;

            var finalComment = string.IsNullOrWhiteSpace(transcription)
                ? comment
                : string.IsNullOrWhiteSpace(comment)
                    ? transcription
                    : string.Concat(comment, "\n\n[Transcription] ", transcription);

            // Serialize log files as attachments.
            var shortId = Guid.NewGuid().ToString("N")[..8];
            RemoveExistingLogMedias(medias);
            await SerializeLogFilesAsync(shortId, logs, browserConsoleLogs, medias);

            var metadata = new CaptureMetadata
            {
                Project = _config.ProjectName ?? "Stratum",
                StartedAt = session.StartedAt,
                FinishedAt = DateTimeOffset.UtcNow,
                Os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                User = Environment.UserName,
                ScreenResolution = "unknown",
                DotNetVersion = System.Runtime.InteropServices.RuntimeEnvironment.GetSystemVersion(),
            };

            // Auto-generate title from AI analysis or comment.
            var autoTitle = GenerateTitle(finalComment, videoAnalysis);

            _preparedBundle = new CaptureBundle
            {
                Id = Guid.NewGuid(),
                Type = session.CaptureType,
                Title = autoTitle,
                Metadata = metadata,
                Medias = medias,
                Logs = logs,
                HttpTraffic = httpTraffic,
                UserActions = userActions,
                Comments = string.IsNullOrWhiteSpace(finalComment)
                    ? []
                    : [finalComment],
                Tags = tags,
                VideoAnalysis = videoAnalysis,
                BrowserConsoleLogs = browserConsoleLogs,
            };

            SetState(CaptureState.Previewing);
        }
        catch
        {
            SetState(CaptureState.Capturing);
            _preparedBundle = null;
            _toastService.ShowError(_localizer["BugCapture_SaveFailed"]);
            throw;
        }
    }

    public void UpdatePreparedBundle(string title, string comment, IReadOnlyList<string> tags, string? aiSummary = null, string? aiSteps = null)
    {
        if (_preparedBundle is null)
        {
            return;
        }

        var videoAnalysis = _preparedBundle.VideoAnalysis;
        if (videoAnalysis is not null && (aiSummary is not null || aiSteps is not null))
        {
            videoAnalysis = videoAnalysis with
            {
                Summary = aiSummary ?? videoAnalysis.Summary,
                StepsToReproduce = aiSteps ?? videoAnalysis.StepsToReproduce,
            };
        }

        _preparedBundle = _preparedBundle with
        {
            Title = title,
            Comments = string.IsNullOrWhiteSpace(comment) ? [] : [comment],
            Tags = tags,
            VideoAnalysis = videoAnalysis,
        };
    }

    public async Task SubmitPreparedAsync()
    {
        if (_preparedBundle is null)
        {
            return;
        }

        SetState(CaptureState.Submitting);

        try
        {
            await _bundleReporter.ReportAsync(_preparedBundle);
            _toastService.ShowSuccess(_localizer["BugCapture_SaveSuccess"]);
        }
        catch
        {
            SetState(CaptureState.Previewing);
            _toastService.ShowError(_localizer["BugCapture_SaveFailed"]);
            throw;
        }

        _preparedBundle = null;
        _session = null;
        ClearCaches();
        SetState(CaptureState.Idle);
    }

    public void ReturnToEditing()
    {
        if (State != CaptureState.Previewing)
        {
            return;
        }

        _preparedBundle = null;

        // Caches remain so expensive operations (transcription, video analysis)
        // are not repeated on the next PrepareAsync call.
        SetState(CaptureState.Capturing);
    }

    public void CancelSession()
    {
        if (_session is null)
        {
            return;
        }

        var session = _session;
        _session = null;
        _preparedBundle = null;
        ClearCaches();

        foreach (var screenshot in session.Screenshots)
        {
            TryDeleteFile(screenshot.FilePath);
        }

        // Best-effort stop console capture.
        _ = _browserConsoleProvider.StopAsync();

        SetState(CaptureState.Idle);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private static string GenerateTitle(string? comment, VideoAnalysis? videoAnalysis)
    {
        // 1. AI-generated title (short and actionable)
        if (videoAnalysis is { Title.Length: > 0 })
        {
            return videoAnalysis.Title.Length <= 80
                ? videoAnalysis.Title
                : string.Concat(videoAnalysis.Title.AsSpan(0, 77), "...");
        }

        // 2. First line of comment if meaningful
        if (!string.IsNullOrWhiteSpace(comment))
        {
            var firstLine = comment.Split('\n', 2, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
            if (firstLine.Length > 0 && !firstLine.StartsWith("[Transcription]", StringComparison.Ordinal))
            {
                return firstLine.Length <= 80 ? firstLine : string.Concat(firstLine.AsSpan(0, 77), "...");
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Removes any previously serialized log MediaCapture entries from the list.
    /// Called before re-serializing logs on a second PrepareAsync (after ReturnToEditing).
    /// </summary>
    private static void RemoveExistingLogMedias(List<MediaCapture> medias)
    {
        medias.RemoveAll(m => m.MediaType == "log");
    }

    private static async Task SerializeLogFilesAsync(
        string shortId,
        List<LogEntry> serverLogs,
        IReadOnlyList<BrowserConsoleEntry> consoleLogs,
        List<MediaCapture> medias)
    {
        var logsDir = Path.Combine(Path.GetTempPath(), "stratum-bugcapture", "logs");
        Directory.CreateDirectory(logsDir);
        var ic = CultureInfo.InvariantCulture;

        // Server logs file.
        if (serverLogs.Count > 0)
        {
            var sb = new StringBuilder();
            foreach (var log in serverLogs)
            {
                sb.AppendLine(ic, $"[{log.Timestamp:HH:mm:ss.fff}] [{log.Level,-5}] [{log.Category}] {log.Message}");
                if (log.Exception is not null)
                {
                    sb.AppendLine(ic, $"  Exception: {log.Exception.Type}: {log.Exception.Message}");
                    if (!string.IsNullOrEmpty(log.Exception.StackTrace))
                    {
                        sb.AppendLine(log.Exception.StackTrace);
                    }
                }
            }

            var fileName = $"server-logs-{shortId}.log";
            var filePath = Path.Combine(logsDir, fileName);
            await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);

            medias.Add(new MediaCapture
            {
                Id = Guid.NewGuid(),
                MediaType = "log",
                FilePath = filePath,
                FileName = fileName,
                MimeType = "text/plain",
                FileSizeBytes = new FileInfo(filePath).Length,
                CapturedAt = DateTimeOffset.UtcNow,
                Description = "Server application logs",
                Sequence = medias.Count,
            });
        }

        // Browser console logs file.
        if (consoleLogs.Count > 0)
        {
            var sb = new StringBuilder();
            foreach (var entry in consoleLogs)
            {
                sb.AppendLine(ic, $"[{entry.Timestamp:HH:mm:ss.fff}] [{entry.Level.ToUpperInvariant(),-5}] {entry.Message}");
            }

            var fileName = $"browser-console-{shortId}.log";
            var filePath = Path.Combine(logsDir, fileName);
            await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);

            medias.Add(new MediaCapture
            {
                Id = Guid.NewGuid(),
                MediaType = "log",
                FilePath = filePath,
                FileName = fileName,
                MimeType = "text/plain",
                FileSizeBytes = new FileInfo(filePath).Length,
                CapturedAt = DateTimeOffset.UtcNow,
                Description = "Browser console logs",
                Sequence = medias.Count,
            });
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Video size {SizeBytes} exceeds the Whisper upload limit (25 MB): narration transcription skipped, video analysis relies on the multimodal model alone.")]
    private static partial void LogTranscriptionSkippedVideoTooLarge(ILogger logger, int sizeBytes);

    /// <summary>
    /// Performs the expensive operations: stops recordings, saves media files,
    /// transcribes audio, analyzes video, and extracts key frames.
    /// Results are cached for potential re-use after ReturnToEditing.
    /// </summary>
    private async Task BuildExpensiveDataAsync(CaptureSession session)
    {
        var screenshots = session.Screenshots;

        for (var i = 0; i < screenshots.Count; i++)
        {
            var old = screenshots[i];
            screenshots[i] = old with { Sequence = i };
        }

        var medias = new List<MediaCapture>(screenshots);

        // Audio
        byte[] audioBytes;

        if (_audioRecorder.IsRecording)
        {
            audioBytes = await _audioRecorder.StopAsync();
        }
        else
        {
            audioBytes = session.AudioBytes ?? [];
        }

        if (audioBytes.Length > 0)
        {
            var audioDir = Path.Combine(Path.GetTempPath(), "stratum-bugcapture", "audio");
            Directory.CreateDirectory(audioDir);
            var audioId = Guid.NewGuid();
            var audioFileName = $"{audioId:N}.webm";
            var audioFilePath = Path.Combine(audioDir, audioFileName);
            await File.WriteAllBytesAsync(audioFilePath, audioBytes);

            medias.Add(new MediaCapture
            {
                Id = audioId,
                MediaType = "audio",
                FilePath = audioFilePath,
                FileName = audioFileName,
                MimeType = "audio/webm",
                FileSizeBytes = audioBytes.Length,
                CapturedAt = DateTimeOffset.UtcNow,
                Description = "Audio recording",
                Sequence = medias.Count,
            });
        }

        // Transcription.
        var transcription = string.Empty;

        if (audioBytes.Length > 0)
        {
            transcription = await _transcriptionService.TranscribeAsync(audioBytes);
        }

        // Video
        byte[] videoBytes;

        if (_screenRecorder.IsRecording)
        {
            videoBytes = await _screenRecorder.StopAsync();
        }
        else
        {
            videoBytes = session.VideoBytes ?? [];
        }

        VideoAnalysis? videoAnalysis = null;

        if (videoBytes.Length > 0)
        {
            var videoDir = Path.Combine(Path.GetTempPath(), "stratum-bugcapture", "video");
            Directory.CreateDirectory(videoDir);
            var videoId = Guid.NewGuid();
            var videoFileName = $"{videoId:N}.webm";
            var videoFilePath = Path.Combine(videoDir, videoFileName);
            await File.WriteAllBytesAsync(videoFilePath, videoBytes);

            medias.Add(new MediaCapture
            {
                Id = videoId,
                MediaType = "video",
                FilePath = videoFilePath,
                FileName = videoFileName,
                MimeType = "video/webm",
                FileSizeBytes = videoBytes.Length,
                CapturedAt = DateTimeOffset.UtcNow,
                Description = "Screen recording",
                Sequence = medias.Count,
            });

            // Liakont: transcribe the video's audio track via Whisper (deterministic) when no
            // separate audio recording exists — the multimodal model's own audio understanding
            // ignores the narration ~2/3 of the time (vérifié sur pièce 2026-06-10). Whisper
            // accepts WebM video directly.
            if (transcription.Length == 0)
            {
                if (videoBytes.Length <= WhisperUploadLimitBytes)
                {
                    transcription = await _transcriptionService.TranscribeAsync(videoBytes);
                }
                else
                {
                    LogTranscriptionSkippedVideoTooLarge(_logger, videoBytes.Length);
                }
            }

            // AI video analysis via OpenRouter/Gemini.
            videoAnalysis = await _videoAnalysisService.AnalyzeAsync(
                videoBytes,
                CultureInfo.CurrentUICulture,
                transcription.Length > 0 ? transcription : null);

            // Extract key frames identified by the AI (best-effort).
            if (videoAnalysis.KeyMoments.Count > 0)
            {
                try
                {
                    var timestamps = videoAnalysis.KeyMoments.Select(m => m.TimestampSeconds).ToList();
                    var descriptions = videoAnalysis.KeyMoments.Select(m => m.Description).ToList();
                    var frames = await _screenshotProvider.ExtractFramesAsync(videoBytes, timestamps, descriptions);

                    foreach (var frame in frames)
                    {
                        medias.Add(frame with { Sequence = medias.Count });
                    }
                }
                catch
                {
                    // Frame extraction is best-effort.
                }
            }
        }

        // Cache all results.
        _cachedMedias = medias;
        _cachedTranscription = transcription;
        _cachedVideoAnalysis = videoAnalysis;
    }

    private void ClearCaches()
    {
        _cachedTranscription = null;
        _cachedVideoAnalysis = null;
        _cachedMedias = null;
    }

    private void SetState(CaptureState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class CaptureSession : ICaptureSession
    {
        private readonly IScreenshotProvider _screenshotProvider;
        private readonly IAudioRecorder _audioRecorder;
        private readonly IScreenRecorder _screenRecorder;

        public CaptureSession(
            CaptureType captureType,
            IScreenshotProvider screenshotProvider,
            IAudioRecorder audioRecorder,
            IScreenRecorder screenRecorder)
        {
            CaptureType = captureType;
            StartedAt = DateTimeOffset.UtcNow;
            _screenshotProvider = screenshotProvider;
            _audioRecorder = audioRecorder;
            _screenRecorder = screenRecorder;
        }

        public CaptureType CaptureType { get; }

        public DateTimeOffset StartedAt { get; }

        public List<MediaCapture> Screenshots { get; } = [];

        public byte[]? AudioBytes { get; private set; }

        public byte[]? VideoBytes { get; private set; }

        public async Task AddScreenshotAsync(string description = "")
        {
            var capture = await _screenshotProvider.CapturePageAsync(description);
            Screenshots.Add(capture);
        }

        public async Task AddAudioAsync(string description = "")
        {
            if (_audioRecorder.IsRecording)
            {
                AudioBytes = await _audioRecorder.StopAsync();
            }
            else
            {
                await _audioRecorder.StartAsync();
            }
        }

        public async Task AddScreenRecordingAsync(string description = "")
        {
            if (_screenRecorder.IsRecording)
            {
                VideoBytes = await _screenRecorder.StopAsync();
            }
            else
            {
                await _screenRecorder.StartAsync();
            }
        }

        public void AddComment(string comment)
        {
            // Comments are collected at PrepareAsync time via the comment parameter.
        }

        public void AddTag(string tag)
        {
            // Tags are collected at PrepareAsync time via the tags parameter.
        }

        public CaptureBundle GetCurrentBundle()
        {
            return new CaptureBundle
            {
                Id = Guid.NewGuid(),
                Type = CaptureType,
                Metadata = new CaptureMetadata
                {
                    Project = "Stratum",
                    StartedAt = StartedAt,
                    FinishedAt = DateTimeOffset.UtcNow,
                    Os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                    User = Environment.UserName,
                    ScreenResolution = "unknown",
                    DotNetVersion = System.Runtime.InteropServices.RuntimeEnvironment.GetSystemVersion(),
                },
                Medias = Screenshots.AsReadOnly(),
                Logs = [],
                HttpTraffic = [],
                UserActions = [],
                Comments = [],
                Tags = [],
            };
        }
    }
}
