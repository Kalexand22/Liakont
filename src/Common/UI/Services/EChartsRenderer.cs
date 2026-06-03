namespace Stratum.Common.UI.Services;

using Microsoft.JSInterop;
using Stratum.Common.UI.Models;

/// <summary>
/// Default <see cref="IChartRenderer"/> implementation using Apache ECharts.
/// Delegates all rendering to the <c>chart.js</c> module via JS interop.
/// </summary>
public sealed class EChartsRenderer : IChartRenderer
{
    public async Task InitializeAsync(
        IJSObjectReference jsModule, string containerId, ChartConfig config)
    {
        var options = BuildOptions(config);
        await jsModule.InvokeVoidAsync("chartInit", containerId, options);
    }

    public async Task UpdateAsync(
        IJSObjectReference jsModule, string containerId, ChartConfig config)
    {
        var options = BuildOptions(config);
        await jsModule.InvokeVoidAsync("chartUpdate", containerId, options);
    }

    public async Task DisposeAsync(
        IJSObjectReference jsModule, string containerId)
    {
        await jsModule.InvokeVoidAsync("chartDispose", containerId);
    }

    private static Dictionary<string, object?> BuildOptions(ChartConfig config)
    {
        var chartType = config.Type;

        return chartType switch
        {
            ChartType.Pie => BuildPieOptions(config),
            ChartType.Radar => BuildRadarOptions(config),
            ChartType.Gauge => BuildGaugeOptions(config),
            _ => BuildCartesianOptions(config),
        };
    }

    private static Dictionary<string, object?> BuildCartesianOptions(ChartConfig config)
    {
        var series = config.Series.Select(s =>
        {
            // s.Type is already resolved by Chart.razor (falls back to chart-level type).
            var effectiveType = s.Type;
            var seriesType = effectiveType switch
            {
                ChartType.Area => "line",
                _ => effectiveType.ToString().ToLowerInvariant(),
            };

            var entry = new Dictionary<string, object?>
            {
                ["name"] = s.Name,
                ["type"] = seriesType,
                ["data"] = s.Values,
            };

            if (effectiveType == ChartType.Area)
            {
                entry["areaStyle"] = new { };
            }

            if (config.Stacked)
            {
                entry["stack"] = "total";
            }

            if (s.Color is not null)
            {
                entry["itemStyle"] = new { color = s.Color };
            }

            return entry;
        }).ToList();

        return new Dictionary<string, object?>
        {
            ["title"] = config.Title is not null ? new { text = config.Title } : null,
            ["tooltip"] = new { trigger = "axis" },
            ["legend"] = config.ShowLegend ? new { show = true } : new { show = false },
            ["xAxis"] = new { type = "category", data = config.Categories },
            ["yAxis"] = new { type = "value" },
            ["series"] = series,
        };
    }

    private static Dictionary<string, object?> BuildPieOptions(ChartConfig config)
    {
        var firstSeries = config.Series.Count > 0 ? config.Series[0] : null;
        var data = new List<object>();

        if (firstSeries is not null)
        {
            for (var i = 0; i < config.Categories.Count && i < firstSeries.Values.Count; i++)
            {
                data.Add(new { name = config.Categories[i], value = firstSeries.Values[i] });
            }
        }

        return new Dictionary<string, object?>
        {
            ["title"] = config.Title is not null ? new { text = config.Title } : null,
            ["tooltip"] = new { trigger = "item" },
            ["legend"] = config.ShowLegend ? new { show = true } : new { show = false },
            ["series"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "pie",
                    ["radius"] = "60%",
                    ["data"] = data,
                    ["name"] = firstSeries?.Name ?? "Series",
                },
            },
        };
    }

    private static Dictionary<string, object?> BuildRadarOptions(ChartConfig config)
    {
        // Derive max for each indicator from the actual data.
        var indicators = config.Categories.Select((c, i) =>
        {
            var maxVal = config.Series
                .Where(s => i < s.Values.Count)
                .Select(s => s.Values[i])
                .DefaultIfEmpty(100.0)
                .Max();

            // Add 20% headroom so points don't sit on the edge.
            return new { name = c, max = Math.Ceiling(maxVal * 1.2) };
        }).ToList();
        var seriesData = config.Series.Select(s => new
        {
            value = s.Values,
            name = s.Name,
        }).ToList();

        return new Dictionary<string, object?>
        {
            ["title"] = config.Title is not null ? new { text = config.Title } : null,
            ["tooltip"] = new { trigger = "item" },
            ["legend"] = config.ShowLegend ? new { show = true } : new { show = false },
            ["radar"] = new { indicator = indicators },
            ["series"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "radar",
                    ["data"] = seriesData,
                },
            },
        };
    }

    private static Dictionary<string, object?> BuildGaugeOptions(ChartConfig config)
    {
        var firstSeries = config.Series.Count > 0 ? config.Series[0] : null;
        var value = firstSeries is not null && firstSeries.Values.Count > 0 ? firstSeries.Values[0] : 0;

        return new Dictionary<string, object?>
        {
            ["title"] = config.Title is not null ? new { text = config.Title } : null,
            ["series"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "gauge",
                    ["data"] = new[] { new { value, name = firstSeries?.Name ?? string.Empty } },
                },
            },
        };
    }
}
