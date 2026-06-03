namespace Stratum.Common.Infrastructure.BugCapture;

using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;

public sealed class BundleWriter : IBundleReporter
{
    private readonly CaptureConfiguration _config;

    public BundleWriter(IOptions<CaptureConfiguration> options)
    {
        _config = options.Value;
    }

    public Task<string> ReportAsync(CaptureBundle bundle) => WriteAsync(bundle);

    public async Task<string> WriteAsync(CaptureBundle bundle)
    {
        var shortId = bundle.Id.ToString("N")[..8];
        var timestamp = bundle.Metadata.StartedAt.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var typeName = bundle.Type.ToString().ToUpperInvariant();
        var dirName = string.Concat(typeName, "_", timestamp, "_", shortId);
        var dirPath = Path.Combine(_config.ReportsPath, dirName);

        Directory.CreateDirectory(dirPath);

        await WriteBundleMarkdownAsync(bundle, dirPath);
        await CopyMediaFilesAsync(bundle, dirPath);

        return dirPath;
    }

    private static async Task WriteBundleMarkdownAsync(CaptureBundle bundle, string dirPath)
    {
        var mdPath = Path.Combine(dirPath, "bundle.md");
        var sb = new StringBuilder();
        var ic = CultureInfo.InvariantCulture;

        sb.AppendLine(ic, $"# {bundle.Type} Report — {bundle.Metadata.Project}");
        sb.AppendLine();

        // Comments
        if (bundle.Comments.Count > 0)
        {
            sb.AppendLine("## Description");
            sb.AppendLine();
            foreach (var comment in bundle.Comments)
            {
                sb.AppendLine(comment);
                sb.AppendLine();
            }
        }

        // Tags
        if (bundle.Tags.Count > 0)
        {
            sb.AppendLine("## Tags");
            sb.AppendLine();
            sb.AppendLine(string.Join(" ", bundle.Tags.Select(t => string.Concat("`", t, "`"))));
            sb.AppendLine();
        }

        // Environment
        sb.AppendLine("## Environment");
        sb.AppendLine();
        sb.AppendLine("| Field | Value |");
        sb.AppendLine("|-------|-------|");
        sb.AppendLine(ic, $"| Project | {bundle.Metadata.Project} |");
        sb.AppendLine(ic, $"| OS | {bundle.Metadata.Os} |");
        sb.AppendLine(ic, $"| User | {bundle.Metadata.User} |");
        sb.AppendLine(ic, $"| Screen | {bundle.Metadata.ScreenResolution} |");
        sb.AppendLine(ic, $"| .NET | {bundle.Metadata.DotNetVersion} |");
        sb.AppendLine(ic, $"| Started | {bundle.Metadata.StartedAt:u} |");
        sb.AppendLine(ic, $"| Finished | {bundle.Metadata.FinishedAt:u} |");
        sb.AppendLine();

        // Media
        if (bundle.Medias.Count > 0)
        {
            sb.AppendLine("## Media");
            sb.AppendLine();
            foreach (var media in bundle.Medias.OrderBy(m => m.Sequence))
            {
                sb.AppendLine(ic, $"### {media.MediaType} #{media.Sequence + 1}");
                if (!string.IsNullOrEmpty(media.Description))
                {
                    sb.AppendLine(ic, $"_{media.Description}_");
                    sb.AppendLine();
                }

                sb.AppendLine(ic, $"- File: `{media.FileName}`");
                sb.AppendLine(ic, $"- Size: {media.FileSizeBytes:N0} bytes");
                sb.AppendLine(ic, $"- Captured: {media.CapturedAt:u}");
                sb.AppendLine();
            }
        }

        // AI Video Analysis
        if (bundle.VideoAnalysis is { Summary.Length: > 0 } analysis)
        {
            sb.AppendLine("## AI Video Analysis");
            sb.AppendLine();
            sb.AppendLine(analysis.Summary);
            sb.AppendLine();

            if (analysis.KeyMoments.Count > 0)
            {
                sb.AppendLine("### Key Moments");
                sb.AppendLine();
                foreach (var moment in analysis.KeyMoments)
                {
                    sb.AppendLine(ic, $"- **{moment.TimestampSeconds:F1}s** — {moment.Description}");
                }

                sb.AppendLine();
            }
        }

        // User actions
        if (bundle.UserActions.Count > 0)
        {
            sb.AppendLine("## User Actions");
            sb.AppendLine();
            sb.AppendLine("| Time | Action | Description |");
            sb.AppendLine("|------|--------|-------------|");
            foreach (var action in bundle.UserActions)
            {
                var relMs = (action.Timestamp - bundle.Metadata.StartedAt).TotalMilliseconds;
                sb.AppendLine(ic, $"| +{relMs:F0}ms | {action.ActionType} | {EscapeMd(action.Description)} |");
            }

            sb.AppendLine();
        }

        // Logs
        if (bundle.Logs.Count > 0)
        {
            sb.AppendLine("## Application Logs");
            sb.AppendLine();
            sb.AppendLine("```");
            foreach (var log in bundle.Logs)
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

            sb.AppendLine("```");
            sb.AppendLine();
        }

        // Browser console logs
        if (bundle.BrowserConsoleLogs.Count > 0)
        {
            sb.AppendLine("## Browser Console Logs");
            sb.AppendLine();
            sb.AppendLine("```");
            foreach (var entry in bundle.BrowserConsoleLogs)
            {
                sb.AppendLine(ic, $"[{entry.Timestamp:HH:mm:ss.fff}] [{entry.Level.ToUpperInvariant(),-5}] {entry.Message}");
            }

            sb.AppendLine("```");
            sb.AppendLine();
        }

        // HTTP traffic
        if (bundle.HttpTraffic.Count > 0)
        {
            sb.AppendLine("## HTTP Traffic");
            sb.AppendLine();
            sb.AppendLine("| Time | Method | URL | Status | Duration |");
            sb.AppendLine("|------|--------|-----|--------|----------|");
            foreach (var req in bundle.HttpTraffic)
            {
                var status = req.StatusCode == 0
                    ? "ERR"
                    : req.StatusCode.ToString(CultureInfo.InvariantCulture);
                var relMs = (req.Timestamp - bundle.Metadata.StartedAt).TotalMilliseconds;
                sb.AppendLine(ic, $"| +{relMs:F0}ms | {req.Method} | {EscapeMd(req.Url)} | {status} | {req.DurationMs}ms |");
            }

            sb.AppendLine();
        }

        await File.WriteAllTextAsync(mdPath, sb.ToString(), Encoding.UTF8);
    }

    private static async Task CopyMediaFilesAsync(CaptureBundle bundle, string dirPath)
    {
        foreach (var media in bundle.Medias)
        {
            if (!File.Exists(media.FilePath))
            {
                continue;
            }

            var destPath = Path.Combine(dirPath, media.FileName);
            await CopyFileAsync(media.FilePath, destPath);
        }
    }

    private static async Task CopyFileAsync(string src, string dest)
    {
        await using var srcStream = new FileStream(
            src,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
        await using var destStream = new FileStream(
            dest,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);
        await srcStream.CopyToAsync(destStream);
    }

    private static string EscapeMd(string s) =>
        s.Replace("|", "\\|", StringComparison.Ordinal)
         .Replace("\n", " ", StringComparison.Ordinal)
         .Replace("\r", string.Empty, StringComparison.Ordinal);
}
