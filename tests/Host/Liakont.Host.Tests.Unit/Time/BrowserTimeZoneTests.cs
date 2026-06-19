namespace Liakont.Host.Tests.Unit.Time;

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.JSInterop;
using Stratum.Common.UI.Time;
using Xunit;

/// <summary>
/// Résolution du fuseau navigateur (RB6) : mapping IANA → <see cref="TimeZoneInfo"/> avec repli UTC sans
/// exception, et mémorisation idempotente par circuit (un seul appel JS, retenté si le JS est indisponible).
/// </summary>
public sealed class BrowserTimeZoneTests
{
    [Fact]
    public void ResolveZone_Maps_A_Known_Iana_Id()
    {
        TimeZoneInfo zone = BrowserTimeZone.ResolveZone("Europe/Paris");

        // Comportemental (l'Id exact varie selon l'OS) : Paris est à UTC+2 en été.
        var utc = new DateTimeOffset(2026, 6, 17, 16, 0, 0, TimeSpan.Zero);
        TimeZoneInfo.ConvertTime(utc, zone).Hour.Should().Be(18);
        zone.Should().NotBe(TimeZoneInfo.Utc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Pas/UnFuseau")]
    public void ResolveZone_Falls_Back_To_Utc_On_Missing_Or_Unknown_Id(string? ianaId)
    {
        BrowserTimeZone.ResolveZone(ianaId).Should().Be(TimeZoneInfo.Utc);
    }

    [Fact]
    public async Task EnsureResolvedAsync_Resolves_Once_And_Memoises()
    {
        var js = new FakeJsRuntime(_ => "Europe/Paris");
        var sut = new BrowserTimeZone();

        sut.IsResolved.Should().BeFalse();
        sut.Zone.Should().BeNull("non résolu avant le 1er appel (pré-rendu) → affichage UTC explicite");

        await sut.EnsureResolvedAsync(js);
        await sut.EnsureResolvedAsync(js);

        sut.IsResolved.Should().BeTrue();
        sut.Zone.Should().NotBeNull();
        js.Calls.Should().Be(1, "le fuseau est mémorisé : un seul aller-retour JS par circuit");
    }

    [Fact]
    public async Task EnsureResolvedAsync_Does_Not_Mark_Resolved_When_Js_Is_Unavailable()
    {
        // Pré-rendu / circuit déconnecté : le JS lève → on ne marque PAS résolu (retentable), Zone reste null.
        var js = new FakeJsRuntime(_ => throw new JSException("JS indisponible (pré-rendu simulé)"));
        var sut = new BrowserTimeZone();

        await sut.EnsureResolvedAsync(js);

        sut.IsResolved.Should().BeFalse();
        sut.Zone.Should().BeNull();

        // Le navigateur répond au cycle suivant → résolution effective.
        var js2 = new FakeJsRuntime(_ => "Europe/Paris");
        await sut.EnsureResolvedAsync(js2);
        sut.IsResolved.Should().BeTrue();
        sut.Zone.Should().NotBeNull();
    }

    [Fact]
    public async Task EnsureResolvedAsync_Raises_Resolved_Once()
    {
        // Les <LiakontDate> s'abonnent à Resolved pour passer en local : il doit être émis exactement une fois.
        var js = new FakeJsRuntime(_ => "Europe/Paris");
        var sut = new BrowserTimeZone();
        var raised = 0;
        sut.Resolved += () => raised++;

        await sut.EnsureResolvedAsync(js);
        await sut.EnsureResolvedAsync(js);

        raised.Should().Be(1);
    }

    [Fact]
    public async Task EnsureResolvedAsync_Maps_An_Unknown_Browser_Id_To_Utc_And_Stops_Retrying()
    {
        // Le navigateur a répondu (IANA exotique) mais le mapping échoue → UTC, et on NE retente PAS (on a une réponse).
        var js = new FakeJsRuntime(_ => "Mars/Olympus_Mons");
        var sut = new BrowserTimeZone();

        await sut.EnsureResolvedAsync(js);

        sut.IsResolved.Should().BeTrue();
        sut.Zone.Should().Be(TimeZoneInfo.Utc);
    }

    private sealed class FakeJsRuntime : IJSRuntime
    {
        private readonly Func<string, object?> _onInvoke;

        public FakeJsRuntime(Func<string, object?> onInvoke) => _onInvoke = onInvoke;

        public int Calls { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            Invoke<TValue>(identifier);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            Invoke<TValue>(identifier);

        private ValueTask<TValue> Invoke<TValue>(string identifier)
        {
            Calls++;
            object? result = _onInvoke(identifier);
            return new ValueTask<TValue>((TValue)result!);
        }
    }
}
