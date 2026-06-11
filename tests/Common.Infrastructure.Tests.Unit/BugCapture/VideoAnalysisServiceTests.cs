namespace Stratum.Common.Infrastructure.Tests.Unit.BugCapture;

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stratum.Common.Infrastructure.BugCapture;
using Xunit;

// Liakont addition (fix BugCapture 2026-06-10, provenance §4.16) : couvre l'injection du
// transcript Whisper dans le prompt d'analyse vidéo — la narration dictée doit atteindre
// le modèle sous forme de texte, pas seulement de piste audio.
public sealed class VideoAnalysisServiceTests
{
    private static readonly byte[] SampleVideo = Encoding.UTF8.GetBytes("fake-webm-bytes");

    [Fact]
    public async Task AnalyzeAsync_Should_Embed_Transcript_In_Prompt_When_Provided()
    {
        var handler = new RecordingHandler();
        var service = CreateService(handler, apiKey: "test-key");

        await service.AnalyzeAsync(
            SampleVideo,
            CultureInfo.GetCultureInfo("fr-FR"),
            "le titre est en double sur toutes les pages");

        handler.LastRequestBody.Should().NotBeNull();
        handler.LastRequestBody.Should().Contain("le titre est en double sur toutes les pages");
        handler.LastRequestBody.Should().Contain("Transcription EXACTE");
    }

    [Fact]
    public async Task AnalyzeAsync_Should_Use_English_Transcript_Block_For_NonFrench_Culture()
    {
        var handler = new RecordingHandler();
        var service = CreateService(handler, apiKey: "test-key");

        await service.AnalyzeAsync(
            SampleVideo,
            CultureInfo.GetCultureInfo("en-US"),
            "the page title is duplicated");

        handler.LastRequestBody.Should().NotBeNull();
        handler.LastRequestBody.Should().Contain("the page title is duplicated");
        handler.LastRequestBody.Should().Contain("EXACT transcript");
    }

    [Fact]
    public async Task AnalyzeAsync_Should_Not_Add_Transcript_Block_When_Transcript_Missing()
    {
        var handler = new RecordingHandler();
        var service = CreateService(handler, apiKey: "test-key");

        await service.AnalyzeAsync(SampleVideo, CultureInfo.GetCultureInfo("fr-FR"));

        handler.LastRequestBody.Should().NotBeNull();
        handler.LastRequestBody.Should().NotContain("Transcription EXACTE");
        handler.LastRequestBody.Should().NotContain("EXACT transcript");
    }

    [Fact]
    public async Task AnalyzeAsync_Should_Skip_Http_Call_When_ApiKey_Missing()
    {
        var handler = new RecordingHandler();
        var service = CreateService(handler, apiKey: null);

        var result = await service.AnalyzeAsync(SampleVideo, CultureInfo.GetCultureInfo("fr-FR"), "transcript");

        handler.LastRequestBody.Should().BeNull();
        result.Summary.Should().BeEmpty();
    }

    private static VideoAnalysisService CreateService(RecordingHandler handler, string? apiKey)
    {
        var config = new CaptureConfiguration { OpenRouterApiKey = apiKey };
        return new VideoAnalysisService(
            new HttpClient(handler),
            Options.Create(config),
            NullLogger<VideoAnalysisService>.Instance);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            const string inner = """{"title": "t", "summary": "s", "steps": "1.", "key_moments": []}""";
            var responseJson = JsonSerializer.Serialize(
                new { choices = new[] { new { message = new { content = inner } } } });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
        }
    }
}
