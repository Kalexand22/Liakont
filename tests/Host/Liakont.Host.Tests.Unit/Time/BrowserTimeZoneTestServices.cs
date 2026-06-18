namespace Liakont.Host.Tests.Unit.Time;

using System;
using Liakont.Host.Time;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Enregistrement du fuseau navigateur pour les tests bUnit des pages/composants migrés (RB6) : un
/// <see cref="FakeBrowserTimeZone"/> RÉSOLU (Europe/Paris par défaut) suffit pour que <c>LiakontDate</c>
/// se résolve en DI et rende l'heure locale. Passer <c>zone: null</c> pour exercer le repli UTC (pré-rendu).
/// </summary>
internal static class BrowserTimeZoneTestServices
{
    public static IServiceCollection AddBrowserTimeZoneStub(this IServiceCollection services, TimeZoneInfo? zone = null) =>
        services.AddSingleton<IBrowserTimeZone>(
            new FakeBrowserTimeZone(zone ?? TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris")));
}
