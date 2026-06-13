namespace Liakont.Host.Tests.Unit.FleetApi;

using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.FleetApi;
using Liakont.Modules.FleetSupervision.Application;
using Liakont.Modules.FleetSupervision.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// Gating de l'endpoint central de heartbeat de flotte (OPS04, acceptance « endpoint sur l'instance
/// mutualisée ») : 404 si le rôle central est désactivé, 401 si la clé d'ingestion est absente/erronée,
/// 400 si l'identifiant d'instance manque, 202 sinon (et le heartbeat est enregistré). Frontière de
/// contrôle d'accès — testée directement sur le handler avec un contexte HTTP factice.
/// </summary>
public sealed class FleetApiEndpointsTests
{
    private const string Key = "central-secret-key";

    [Fact]
    public async Task Returns_404_When_Central_Disabled()
    {
        var ctx = Context(JsonBody("inst-1"), key: Key);
        var ingestor = new RecordingIngestor();

        int status = await InvokeAsync(ctx, ingestor, Options(enabled: false));

        status.Should().Be(StatusCodes.Status404NotFound);
        ingestor.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong-key")]
    public async Task Returns_401_When_Key_Missing_Or_Wrong(string? providedKey)
    {
        var ctx = Context(JsonBody("inst-1"), key: providedKey);
        var ingestor = new RecordingIngestor();

        int status = await InvokeAsync(ctx, ingestor, Options(enabled: true));

        status.Should().Be(StatusCodes.Status401Unauthorized);
        ingestor.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Returns_400_When_InstanceId_Missing()
    {
        var ctx = Context(JsonBody(instanceId: string.Empty), key: Key);
        var ingestor = new RecordingIngestor();

        int status = await InvokeAsync(ctx, ingestor, Options(enabled: true));

        status.Should().Be(StatusCodes.Status400BadRequest);
        ingestor.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Returns_202_And_Records_When_Authorized_And_Valid()
    {
        var ctx = Context(JsonBody("inst-42"), key: Key);
        var ingestor = new RecordingIngestor();

        int status = await InvokeAsync(ctx, ingestor, Options(enabled: true));

        status.Should().Be(StatusCodes.Status202Accepted);
        ingestor.Calls.Should().Be(1);
        ingestor.Last!.InstanceId.Should().Be("inst-42");
    }

    private static async Task<int> InvokeAsync(HttpContext ctx, IFleetHeartbeatIngestor ingestor, IOptions<FleetSupervisionOptions> options)
    {
        IResult result = await FleetApiEndpoints.HandleHeartbeatAsync(ctx, ingestor, options, CancellationToken.None);
        await result.ExecuteAsync(ctx);
        return ctx.Response.StatusCode;
    }

    private static IOptions<FleetSupervisionOptions> Options(bool enabled) =>
        Microsoft.Extensions.Options.Options.Create(new FleetSupervisionOptions
        {
            Central = new FleetCentralOptions { Enabled = enabled, IngestionKey = Key },
        });

    private static string JsonBody(string instanceId) =>
        $"{{\"instanceId\":\"{instanceId}\",\"version\":\"1.4.0\",\"hostingMode\":\"Operated\"}}";

    private static DefaultHttpContext Context(string json, string? key)
    {
        var ctx = new DefaultHttpContext
        {
            // L'exécution d'un IResult (NotFound/Accepted/…) résout ILoggerFactory depuis RequestServices.
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        if (key is not null)
        {
            ctx.Request.Headers[FleetApiHeaders.Key] = key;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(json);
        ctx.Request.Body = new MemoryStream(bytes);
        ctx.Request.ContentType = "application/json";
        ctx.Request.ContentLength = bytes.Length;
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private sealed class RecordingIngestor : IFleetHeartbeatIngestor
    {
        public int Calls { get; private set; }

        public InstanceHeartbeatReport? Last { get; private set; }

        public Task RecordAsync(InstanceHeartbeatReport report, CancellationToken cancellationToken = default)
        {
            Calls++;
            Last = report;
            return Task.CompletedTask;
        }
    }
}
