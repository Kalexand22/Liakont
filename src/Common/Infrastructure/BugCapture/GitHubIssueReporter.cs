namespace Stratum.Common.Infrastructure.BugCapture;

using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Reports a <see cref="CaptureBundle"/> by creating a GitHub Issue via the REST API.
/// Falls back to <see cref="BundleWriter"/> (local disk) when the GitHub API is
/// unreachable or misconfigured.
/// </summary>
public sealed partial class GitHubIssueReporter : IBundleReporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IBundleReporter _fallback;
    private readonly CaptureConfiguration _config;
    private readonly ILogger<GitHubIssueReporter> _logger;

    public GitHubIssueReporter(
        IHttpClientFactory httpClientFactory,
        BundleWriter fallback,
        IOptions<CaptureConfiguration> options,
        ILogger<GitHubIssueReporter> logger)
        : this(httpClientFactory, (IBundleReporter)fallback, options, logger)
    {
    }

    internal GitHubIssueReporter(
        IHttpClientFactory httpClientFactory,
        IBundleReporter fallback,
        IOptions<CaptureConfiguration> options,
        ILogger<GitHubIssueReporter> logger)
    {
        _httpClientFactory = httpClientFactory;
        _fallback = fallback;
        _config = options.Value;
        _logger = logger;
    }

    public async Task<string> ReportAsync(CaptureBundle bundle)
    {
        var gh = _config.GitHub;

        if (gh is null || string.IsNullOrWhiteSpace(gh.Token)
                       || string.IsNullOrWhiteSpace(gh.Owner)
                       || string.IsNullOrWhiteSpace(gh.Repo))
        {
            LogConfigIncomplete(_logger);
            return await _fallback.ReportAsync(bundle);
        }

        try
        {
            var title = BuildTitle(bundle);
            var imageUrls = await UploadMediaAsync(gh.Owner, gh.Repo, gh.Token!, bundle);
            var body = BuildBody(bundle, imageUrls);
            var labels = BuildLabels(bundle, gh);

            var issueUrl = await CreateIssueAsync(gh.Owner, gh.Repo, gh.Token!, title, body, labels);
            LogIssueCreated(_logger, issueUrl);
            return issueUrl;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                        or IOException or InvalidOperationException or KeyNotFoundException)
        {
            LogCreateIssueFailed(_logger, ex);
            return await _fallback.ReportAsync(bundle);
        }
    }

    internal static string BuildTitle(CaptureBundle bundle)
    {
        var prefix = bundle.Type == CaptureType.Bug ? "[Bug]" : "[Feature]";

        // 1. Explicit title (set/edited by user in preview)
        if (!string.IsNullOrWhiteSpace(bundle.Title))
        {
            return $"{prefix} {Truncate(bundle.Title.ReplaceLineEndings(" "), 80)}";
        }

        // 2. First comment line
        if (bundle.Comments.Count > 0 && !string.IsNullOrWhiteSpace(bundle.Comments[0]))
        {
            var firstLine = bundle.Comments[0].Split('\n', 2)[0].Trim();
            if (firstLine.Length > 0)
            {
                return $"{prefix} {Truncate(firstLine, 80)}";
            }
        }

        // 3. Fallback
        var fallback = string.Create(CultureInfo.InvariantCulture, $"{prefix} Capture {bundle.Id:N}");
        return Truncate(fallback, 30);
    }

    internal static string BuildBody(CaptureBundle bundle, IReadOnlyDictionary<string, string>? imageUrls = null)
    {
        var sb = new StringBuilder();
        var ic = CultureInfo.InvariantCulture;

        // Description
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

        // Screenshots (uploaded to repo)
        AppendScreenshots(sb, bundle, imageUrls);

        // Video (uploaded to repo)
        AppendVideo(sb, bundle, imageUrls);

        // Steps to reproduce (from AI or included in description)
        if (bundle.VideoAnalysis is { StepsToReproduce.Length: > 0 } stepsAnalysis)
        {
            sb.AppendLine("## Steps to Reproduce");
            sb.AppendLine();
            sb.AppendLine(stepsAnalysis.StepsToReproduce);
            sb.AppendLine();
        }

        // AI Video Analysis
        if (bundle.VideoAnalysis is { Summary.Length: > 0 } analysis)
        {
            sb.AppendLine("## AI Analysis");
            sb.AppendLine();
            sb.AppendLine(analysis.Summary);
            sb.AppendLine();

            if (analysis.KeyMoments.Count > 0)
            {
                sb.AppendLine("<details><summary>Key Moments</summary>");
                sb.AppendLine();
                foreach (var moment in analysis.KeyMoments)
                {
                    sb.AppendLine(ic, $"- **{moment.TimestampSeconds:F1}s** — {moment.Description}");
                }

                sb.AppendLine();
                sb.AppendLine("</details>");
                sb.AppendLine();
            }
        }

        // User actions
        if (bundle.UserActions.Count > 0)
        {
            sb.AppendLine("## User Actions");
            sb.AppendLine();
            sb.AppendLine("<details><summary>Show user actions</summary>");
            sb.AppendLine();
            sb.AppendLine("| Time | Action | Description |");
            sb.AppendLine("|------|--------|-------------|");
            foreach (var action in bundle.UserActions)
            {
                var relMs = (action.Timestamp - bundle.Metadata.StartedAt).TotalMilliseconds;
                sb.AppendLine(ic, $"| +{relMs:F0}ms | {action.ActionType} | {EscapeMd(action.Description)} |");
            }

            sb.AppendLine();
            sb.AppendLine("</details>");
            sb.AppendLine();
        }

        // Logs
        if (bundle.Logs.Count > 0)
        {
            sb.AppendLine("## Application Logs");
            sb.AppendLine();
            AppendLogFileLink(sb, "server-logs", bundle, imageUrls);
            sb.AppendLine("<details><summary>Show logs</summary>");
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
            sb.AppendLine("</details>");
            sb.AppendLine();
        }

        // Browser console logs
        if (bundle.BrowserConsoleLogs.Count > 0)
        {
            sb.AppendLine("## Browser Console Logs");
            sb.AppendLine();
            AppendLogFileLink(sb, "browser-console", bundle, imageUrls);
            sb.AppendLine("<details><summary>Show console logs</summary>");
            sb.AppendLine();
            sb.AppendLine("```");
            foreach (var entry in bundle.BrowserConsoleLogs)
            {
                sb.AppendLine(ic, $"[{entry.Timestamp:HH:mm:ss.fff}] [{entry.Level.ToUpperInvariant(),-5}] {entry.Message}");
            }

            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("</details>");
            sb.AppendLine();
        }

        // HTTP traffic
        if (bundle.HttpTraffic.Count > 0)
        {
            sb.AppendLine("## HTTP Traffic");
            sb.AppendLine();
            sb.AppendLine("<details><summary>Show HTTP traffic</summary>");
            sb.AppendLine();
            sb.AppendLine("| Time | Method | URL | Status | Duration |");
            sb.AppendLine("|------|--------|-----|--------|----------|");
            foreach (var req in bundle.HttpTraffic)
            {
                var status = req.StatusCode == 0
                    ? "ERR"
                    : req.StatusCode.ToString(ic);
                var relMs = (req.Timestamp - bundle.Metadata.StartedAt).TotalMilliseconds;
                sb.AppendLine(ic, $"| +{relMs:F0}ms | {req.Method} | {EscapeMd(req.Url)} | {status} | {req.DurationMs}ms |");
            }

            sb.AppendLine();
            sb.AppendLine("</details>");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine("_Generated by Stratum BugCapture_");

        return sb.ToString();
    }

    internal static List<string> BuildLabels(CaptureBundle bundle, GitHubIssueConfiguration config)
    {
        var labels = new List<string>(config.DefaultLabels);
        labels.Add(bundle.Type == CaptureType.Bug ? "bug" : "enhancement");
        return labels;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "GitHub Issue configuration is incomplete; falling back to local disk")]
    private static partial void LogConfigIncomplete(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created GitHub Issue: {IssueUrl}")]
    private static partial void LogIssueCreated(ILogger logger, string issueUrl);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to create GitHub Issue; falling back to local disk")]
    private static partial void LogCreateIssueFailed(ILogger logger, Exception exception);

    private static void AppendScreenshots(StringBuilder sb, CaptureBundle bundle, IReadOnlyDictionary<string, string>? imageUrls)
    {
        var screenshots = bundle.Medias
            .Where(m => m.MediaType == "screenshot")
            .OrderBy(m => m.Sequence)
            .ToList();

        if (screenshots.Count == 0)
        {
            return;
        }

        var ic = CultureInfo.InvariantCulture;

        sb.AppendLine("## Screenshots");
        sb.AppendLine();

        foreach (var screenshot in screenshots)
        {
            var desc = !string.IsNullOrEmpty(screenshot.Description)
                ? screenshot.Description
                : $"Screenshot {screenshot.Sequence + 1}";

            // Embed the image if it was uploaded to the repo.
            if (imageUrls is not null && imageUrls.TryGetValue(screenshot.FileName, out var url))
            {
                sb.AppendLine(ic, $"### {desc}");
                sb.AppendLine();
                sb.AppendLine(ic, $"![{desc}]({url})");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine(ic, $"- **{desc}**");
                sb.AppendLine(ic, $"  Size: {screenshot.FileSizeBytes:N0} bytes | Captured: {screenshot.CapturedAt:u}");
                sb.AppendLine();
            }
        }
    }

    private static void AppendVideo(StringBuilder sb, CaptureBundle bundle, IReadOnlyDictionary<string, string>? mediaUrls)
    {
        var videos = bundle.Medias
            .Where(m => m.MediaType == "video")
            .OrderBy(m => m.Sequence)
            .ToList();

        if (videos.Count == 0)
        {
            return;
        }

        var ic = CultureInfo.InvariantCulture;

        sb.AppendLine("## Screen Recording");
        sb.AppendLine();

        foreach (var video in videos)
        {
            if (mediaUrls is not null && mediaUrls.TryGetValue(video.FileName, out var url))
            {
                // GitHub renders video links as embedded players.
                sb.AppendLine(ic, $"[{video.FileName}]({url})");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine(ic, $"- **{video.FileName}** — {video.FileSizeBytes:N0} bytes");
                sb.AppendLine();
            }
        }
    }

    private static void AppendLogFileLink(
        StringBuilder sb,
        string filePrefix,
        CaptureBundle bundle,
        IReadOnlyDictionary<string, string>? mediaUrls)
    {
        if (mediaUrls is null)
        {
            return;
        }

        var logMedia = bundle.Medias.FirstOrDefault(
            m => m.MediaType == "log" && m.FileName.StartsWith(filePrefix, StringComparison.Ordinal));

        if (logMedia is not null && mediaUrls.TryGetValue(logMedia.FileName, out var url))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"[Full log file]({url})");
            sb.AppendLine();
        }
    }

    private static string Truncate(string s, int maxLength) =>
        s.Length <= maxLength ? s : string.Concat(s.AsSpan(0, maxLength - 3), "...");

    private static string EscapeMd(string s) =>
        s.Replace("|", "\\|", StringComparison.Ordinal)
         .Replace("\n", " ", StringComparison.Ordinal)
         .Replace("\r", string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// Ensures a <c>bug-captures</c> branch exists in the repo for media uploads.
    /// Creates it from the default branch HEAD if missing.
    /// </summary>
    private static async Task<string> EnsureBugCapturesBranchAsync(
        HttpClient client, string owner, string repo, string token)
    {
        const string branchName = "bug-captures";

        try
        {
            // Check if branch already exists.
            using var check = new HttpRequestMessage(
                HttpMethod.Get,
                $"/repos/{owner}/{repo}/git/ref/heads/{branchName}");
            check.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var checkResponse = await client.SendAsync(check);
            if (checkResponse.IsSuccessStatusCode)
            {
                return branchName;
            }

            // Get default branch SHA.
            using var repoReq = new HttpRequestMessage(HttpMethod.Get, $"/repos/{owner}/{repo}");
            repoReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var repoResp = await client.SendAsync(repoReq);
            if (!repoResp.IsSuccessStatusCode)
            {
                return branchName;
            }

            using var repoDoc = await JsonDocument.ParseAsync(await repoResp.Content.ReadAsStreamAsync());
            var defaultBranch = repoDoc.RootElement.GetProperty("default_branch").GetString() ?? "main";

            using var shaReq = new HttpRequestMessage(
                HttpMethod.Get,
                $"/repos/{owner}/{repo}/git/ref/heads/{defaultBranch}");
            shaReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var shaResp = await client.SendAsync(shaReq);
            if (!shaResp.IsSuccessStatusCode)
            {
                return branchName;
            }

            using var shaDoc = await JsonDocument.ParseAsync(await shaResp.Content.ReadAsStreamAsync());
            var sha = shaDoc.RootElement.GetProperty("object").GetProperty("sha").GetString();

            // Create the branch.
            var createPayload = new { @ref = $"refs/heads/{branchName}", sha };
            using var createReq = new HttpRequestMessage(
                HttpMethod.Post,
                $"/repos/{owner}/{repo}/git/refs");
            createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            createReq.Content = JsonContent.Create(createPayload, options: JsonOptions);
            await client.SendAsync(createReq);
        }
        catch
        {
            // Best-effort; if branch creation fails, upload will try anyway.
        }

        return branchName;
    }

    /// <summary>
    /// Uploads media files to the repo under .bug-captures/{bundleId}/ via the Contents API.
    /// Returns a dictionary mapping FileName → raw download URL.
    /// </summary>
    private async Task<Dictionary<string, string>> UploadMediaAsync(
        string owner, string repo, string token, CaptureBundle bundle)
    {
        var urls = new Dictionary<string, string>();
        var uploadable = bundle.Medias
            .Where(m => m.MediaType is "screenshot" or "video" or "log")
            .ToList();

        if (uploadable.Count == 0)
        {
            return urls;
        }

        using var client = _httpClientFactory.CreateClient(nameof(GitHubIssueReporter));
        var shortId = bundle.Id.ToString("N")[..8];

        // Ensure the bug-captures branch exists (avoids 409 on protected default branch).
        var branch = await EnsureBugCapturesBranchAsync(client, owner, repo, token);

        foreach (var media in uploadable)
        {
            if (!File.Exists(media.FilePath))
            {
                continue;
            }

            try
            {
                var fileBytes = await File.ReadAllBytesAsync(media.FilePath);
                var base64 = Convert.ToBase64String(fileBytes);
                var path = $".bug-captures/{shortId}/{media.FileName}";

                var payload = new { message = $"bug-capture: {shortId} — {media.FileName}", content = base64, branch };
                var url = $"/repos/{owner}/{repo}/contents/{path}";
                using var request = new HttpRequestMessage(HttpMethod.Put, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Content = JsonContent.Create(payload, options: JsonOptions);

                using var response = await client.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
                    if (doc.RootElement.TryGetProperty("content", out var content)
                        && content.TryGetProperty("download_url", out var downloadUrl)
                        && downloadUrl.GetString() is { } rawUrl)
                    {
                        urls[media.FileName] = rawUrl;
                    }
                }
            }
            catch
            {
                // Best-effort upload; media will be listed as metadata if upload fails.
            }
        }

        return urls;
    }

    private async Task<string> CreateIssueAsync(
        string owner, string repo, string token, string title, string body, List<string> labels)
    {
        using var client = _httpClientFactory.CreateClient(nameof(GitHubIssueReporter));

        var payload = new { title, body, labels };
        var url = $"/repos/{owner}/{repo}/issues";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(payload, options: JsonOptions);

        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        if (doc.RootElement.TryGetProperty("html_url", out var urlElement)
            && urlElement.GetString() is { } htmlUrl)
        {
            return htmlUrl;
        }

        throw new InvalidOperationException("GitHub response missing html_url");
    }
}
