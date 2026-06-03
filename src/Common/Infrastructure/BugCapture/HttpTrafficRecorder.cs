namespace Stratum.Common.Infrastructure.BugCapture;

using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Options;

public sealed class HttpTrafficRecorder : DelegatingHandler, IHttpTrafficRecorder
{
    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Cookie",
        "Set-Cookie",
        "X-Api-Key",
    };

    private readonly ConcurrentQueue<HttpTrafficRecord> _buffer = new();
    private readonly CaptureConfiguration _config;

    public HttpTrafficRecorder(IOptions<CaptureConfiguration> options)
    {
        _config = options.Value;
    }

    public IReadOnlyList<HttpTrafficRecord> GetSnapshot(DateTimeOffset since)
    {
        return _buffer
            .Where(r => r.Timestamp >= since)
            .ToList();
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var sw = Stopwatch.StartNew();

        string? requestBody = null;
        var requestBodyTruncated = false;

        if (request.Content is not null)
        {
            (requestBody, requestBodyTruncated) = await ReadBodyAsync(
                request.Content, cancellationToken);
        }

        var requestHeaders = FilterHeaders(request.Headers
            .Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>()));

        HttpResponseMessage response;

        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch
        {
            sw.Stop();

            var errorRecord = new HttpTrafficRecord
            {
                Timestamp = timestamp,
                Method = request.Method.Method,
                Url = request.RequestUri?.ToString() ?? string.Empty,
                StatusCode = 0,
                DurationMs = sw.ElapsedMilliseconds,
                RequestHeaders = requestHeaders,
                RequestBody = requestBody,
                RequestBodyTruncated = requestBodyTruncated,
                IsError = true,
            };

            Enqueue(errorRecord);
            throw;
        }

        sw.Stop();

        string? responseBody = null;
        var responseBodyTruncated = false;

        if (response.Content is not null)
        {
            (responseBody, responseBodyTruncated) = await ReadBodyAsync(
                response.Content, cancellationToken);
        }

        var responseHeaders = FilterHeaders(
            response.Headers.Concat(response.Content?.Headers
                ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>()));

        var record = new HttpTrafficRecord
        {
            Timestamp = timestamp,
            Method = request.Method.Method,
            Url = request.RequestUri?.ToString() ?? string.Empty,
            StatusCode = (int)response.StatusCode,
            DurationMs = sw.ElapsedMilliseconds,
            RequestHeaders = requestHeaders,
            RequestBody = requestBody,
            RequestBodyTruncated = requestBodyTruncated,
            ResponseHeaders = responseHeaders,
            ResponseBody = responseBody,
            ResponseBodyTruncated = responseBodyTruncated,
            IsError = !response.IsSuccessStatusCode,
        };

        Enqueue(record);
        return response;
    }

    private static Dictionary<string, string>? FilterHeaders(
        IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
    {
        var filtered = headers
            .Where(h => !SensitiveHeaders.Contains(h.Key))
            .ToDictionary(
                h => h.Key,
                h => string.Join(", ", h.Value),
                StringComparer.OrdinalIgnoreCase);

        return filtered.Count > 0 ? filtered : null;
    }

    private void Enqueue(HttpTrafficRecord record)
    {
        _buffer.Enqueue(record);

        if (_buffer.Count > 2 * _config.MaxHttpRecords)
        {
            while (_buffer.Count > _config.MaxHttpRecords)
            {
                _buffer.TryDequeue(out _);
            }
        }
    }

    private async Task<(string? Body, bool Truncated)> ReadBodyAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await content.ReadAsByteArrayAsync(cancellationToken);

            if (bytes.Length == 0)
            {
                return (null, false);
            }

            if (bytes.Length > _config.MaxBodySizeBytes)
            {
                var truncated = System.Text.Encoding.UTF8.GetString(bytes, 0, _config.MaxBodySizeBytes);
                return (truncated, true);
            }

            return (System.Text.Encoding.UTF8.GetString(bytes), false);
        }
        catch
        {
            return (null, false);
        }
    }
}
