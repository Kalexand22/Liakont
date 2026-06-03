namespace Stratum.Common.UI;

using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuestPDF.Infrastructure;
using Stratum.Common.Abstractions.Display;
using Stratum.Common.Infrastructure.BugCapture;
using Stratum.Common.UI.Services;
using Stratum.Common.UI.Services.BugCapture;
using Stratum.Common.UI.Services.Filters;

/// <summary>DI registration for Common.UI services (toast, connection status).</summary>
public static class CommonUIServiceExtensions
{
    /// <summary>
    /// Registers Common.UI services:
    /// <list type="bullet">
    ///   <item><see cref="FormErrors"/> — transient form validation error tracker.</item>
    ///   <item><see cref="IToastService"/> — scoped toast queue.</item>
    ///   <item><see cref="IConnectionStatusService"/> — scoped circuit connection tracker.</item>
    ///   <item><see cref="ICommandRegistry"/> / <see cref="IShortcutService"/> — keyboard shortcut system.</item>
    /// </list>
    /// Add <c>&lt;Toast /&gt;</c>, <c>&lt;ConnectionStatus /&gt;</c>, and <c>&lt;GlobalShortcutHandler /&gt;</c>
    /// to your layout after calling this.
    /// </summary>
    public static IServiceCollection AddCommonUI(this IServiceCollection services, IConfiguration? configuration = null)
    {
        // BugCapture services
        if (configuration is not null)
        {
            services.Configure<CaptureConfiguration>(configuration.GetSection("BugCapture"));
            services.Configure<GridFilterAIConfiguration>(configuration.GetSection("GridFilterAI"));
        }

        services.AddSingleton<ClientLogProvider>();
        services.AddSingleton<IClientLogProvider>(sp => sp.GetRequiredService<ClientLogProvider>());
        services.AddSingleton<ILoggerProvider>(sp => sp.GetRequiredService<ClientLogProvider>());
        services.AddScoped<IUserActionLogger, UserActionLogger>();
        services.AddScoped<IScreenshotProvider, ScreenshotProvider>();
        services.AddScoped<IAudioRecorder, AudioRecorder>();
        services.AddScoped<IScreenRecorder, ScreenRecorder>();
        services.AddScoped<IBrowserConsoleProvider, BrowserConsoleProvider>();
        services.AddScoped<BundleWriter>();
        services.AddHttpClient(nameof(GitHubIssueReporter), (sp, client) =>
        {
            client.BaseAddress = new Uri("https://api.github.com");
            client.Timeout = TimeSpan.FromMinutes(2);
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            client.DefaultRequestHeaders.UserAgent.Add(
                new System.Net.Http.Headers.ProductInfoHeaderValue("Stratum-BugCapture", "1.0"));
        });
        services.AddScoped<GitHubIssueReporter>();
        services.AddScoped<IBundleReporter>(sp =>
        {
            var config = sp.GetRequiredService<IOptions<CaptureConfiguration>>().Value;
            if (config.GitHub is { Token: not null and not "", Owner: not null and not "", Repo: not null and not "" })
            {
                return sp.GetRequiredService<GitHubIssueReporter>();
            }

            return sp.GetRequiredService<BundleWriter>();
        });
        services.TryAddScoped<Services.BugCapture.IToastService, NullToastService>();

        // HttpTrafficRecorder is a DelegatingHandler for the HttpClient pipeline
        // but also exposes IHttpTrafficRecorder.GetSnapshot() for BugCaptureService.
        // Register the instance once; resolve both the handler and the interface from it.
        services.AddScoped<HttpTrafficRecorder>();
        services.AddScoped<IHttpTrafficRecorder>(sp => sp.GetRequiredService<HttpTrafficRecorder>());

        // TranscriptionService requires an HttpClient for Whisper API calls.
        services.AddHttpClient<TranscriptionService>();

        // VideoAnalysisService requires an HttpClient for OpenRouter API calls.
        services.AddHttpClient<VideoAnalysisService>();

        // Grid filter AI assistant (GFI11). The provider uses OpenRouter by default;
        // when no API key is configured, IGridFilterAIService.IsAvailable is false and
        // the natural-language filter panel falls back to a disabled state.
        services.AddHttpClient<IGridFilterAIProvider, OpenRouterGridFilterAIProvider>();
        services.TryAddScoped<IGridFilterAIService, GridFilterAIService>();

        services.AddScoped<IBugCaptureService, BugCaptureService>();

        QuestPDF.Settings.License = LicenseType.Community;

        services.AddTransient<IFormErrors, FormErrors>();
        services.AddScoped<Services.IToastService, ToastService>();

        // ConnectionStatusService doubles as a CircuitHandler and an IConnectionStatusService.
        // Register the concrete type once; resolve both abstractions from the same scoped instance.
        services.AddScoped<ConnectionStatusService>();
        services.AddScoped<CircuitHandler>(sp => sp.GetRequiredService<ConnectionStatusService>());
        services.AddScoped<IConnectionStatusService>(sp => sp.GetRequiredService<ConnectionStatusService>());

        // CircuitPresenceHandler cleans up collaboration presence on circuit disconnect.
        // CircuitPresenceRegistry tracks virtual circuit IDs within the Blazor circuit scope.
        services.AddScoped<CircuitPresenceRegistry>();
        services.AddScoped<CircuitPresenceHandler>();
        services.AddScoped<CircuitHandler>(sp => sp.GetRequiredService<CircuitPresenceHandler>());

        // ShortcutService implements both ICommandRegistry and IShortcutService.
        // Register the concrete type once; resolve both abstractions from the same scoped instance.
        services.AddScoped<ShortcutService>();
        services.AddScoped<ICommandRegistry>(sp => sp.GetRequiredService<ShortcutService>());
        services.AddScoped<IShortcutService>(sp => sp.GetRequiredService<ShortcutService>());

        // Navigation tab manager — circuit-scoped tab bar state.
        services.AddScoped<ITabManagerService, TabManagerService>();

        // Tab title provider — fallback; Host should register a localized impl before this.
        services.TryAddScoped<ITabTitleProvider, DefaultTabTitleProvider>();

        // Display template registry — resolves IDisplayTemplate<T> from DI for entity formatting.
        services.AddSingleton<IDisplayTemplateRegistry, DisplayTemplateRegistry>();

        // FieldChangeMediator<T> — bridges Blazor form field changes to IFieldChangeEngine with debounce.
        // Open-generic scoped: one instance per circuit per entity type.
        services.AddScoped(typeof(FieldChangeMediator<>));

        // IPersistentSelectionService<T> — session-scoped selection store that survives
        // page/filter changes (GUX03). Open-generic scoped: one instance per circuit per key type.
        services.AddScoped(typeof(IPersistentSelectionService<>), typeof(PersistentSelectionService<>));

        // Chart renderer — vendor-agnostic chart rendering (default: ECharts).
        services.TryAddScoped<IChartRenderer, EChartsRenderer>();

        // Form registry — maps (entityType, contextKey?) → form component type (GUX15).
        services.TryAddSingleton<IFormRegistry, FormRegistry>();

        return services;
    }
}
