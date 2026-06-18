namespace Liakont.Host.Tests.Unit.Time;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using Stratum.Common.UI.Time;

/// <summary>
/// Double de test de <see cref="IBrowserTimeZone"/> : fuseau fixé d'emblée (résolu) ou nul (pré-rendu),
/// avec <see cref="ResolveTo"/> pour simuler la résolution tardive par la sonde (déclenche l'événement et
/// le re-rendu des <c>LiakontDate</c>). Aucun JS réel (<see cref="EnsureResolvedAsync"/> est un no-op).
/// </summary>
internal sealed class FakeBrowserTimeZone : IBrowserTimeZone
{
    public FakeBrowserTimeZone(TimeZoneInfo? zone)
    {
        Zone = zone;
        IsResolved = zone is not null;
    }

    public event Action? Resolved;

    public TimeZoneInfo? Zone { get; private set; }

    public bool IsResolved { get; private set; }

    public Task EnsureResolvedAsync(IJSRuntime js, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <summary>Simule la résolution tardive du fuseau (sonde du shell) : pose le fuseau et émet l'événement.</summary>
    public void ResolveTo(TimeZoneInfo zone)
    {
        Zone = zone;
        IsResolved = true;
        Resolved?.Invoke();
    }
}
