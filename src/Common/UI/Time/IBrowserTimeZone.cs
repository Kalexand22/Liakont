// Liakont addition (affichage des dates cote navigateur) - not part of the original Stratum vendoring.
namespace Stratum.Common.UI.Time;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.JSInterop;

/// <summary>
/// Fuseau horaire du NAVIGATEUR de l'opérateur (RB6), résolu UNE fois par circuit Blazor Server et mémorisé.
/// Le serveur tourne en UTC (conteneur Docker) : <c>DateTime.ToLocalTime()</c> côté serveur ne convertit donc
/// rien — il faut le fuseau du client. Le résultat est exposé de façon SYNCHRONE (<see cref="Zone"/>) pour que
/// le formatage de date dans le rendu Razor (synchrone) puisse l'utiliser ; la résolution (asynchrone, JS
/// interop) est déclenchée par les composants dans <c>OnAfterRenderAsync(firstRender)</c>.
/// <para>Enregistré en <c>AddScoped</c> (un fuseau par circuit/utilisateur — JAMAIS Singleton, qui mélangerait
/// les fuseaux entre opérateurs). Placé dans le socle <c>Stratum.Common.UI</c> pour être consommé par les pages
/// Host (Liakont) ET les pages d'admin du socle (Stratum.Modules.*) sans inverser la dépendance socle→app.</para>
/// </summary>
public interface IBrowserTimeZone
{
    /// <summary>
    /// Émis UNE fois, quand le fuseau vient d'être résolu. Les afficheurs de date (<c>LiakontDate</c>) s'y
    /// abonnent pour se re-rendre en heure locale dès que la sonde du layout a résolu le fuseau du navigateur.
    /// </summary>
    event Action? Resolved;

    /// <summary>Fuseau du navigateur, ou <c>null</c> tant qu'il n'a pas été résolu (pré-rendu / avant le 1er render).</summary>
    TimeZoneInfo? Zone { get; }

    /// <summary>Vrai dès que la résolution a abouti (y compris repli sur UTC) — la résolution n'est alors plus retentée.</summary>
    bool IsResolved { get; }

    /// <summary>
    /// Résout le fuseau du navigateur via JS interop si ce n'est pas déjà fait (idempotent). À appeler depuis
    /// <c>OnAfterRenderAsync(firstRender)</c> (le JS n'est pas disponible avant). N'échoue jamais : un JS
    /// indisponible (pré-rendu, circuit déconnecté) laisse <see cref="Zone"/> nul et sera retenté au prochain appel.
    /// </summary>
    Task EnsureResolvedAsync(IJSRuntime js, CancellationToken cancellationToken = default);
}
