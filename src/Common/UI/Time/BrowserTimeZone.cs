// Liakont addition (affichage des dates cote navigateur) - not part of the original Stratum vendoring.
namespace Stratum.Common.UI.Time;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.JSInterop;

/// <summary>
/// Implémentation scopée (par circuit) de <see cref="IBrowserTimeZone"/> : interroge le navigateur via
/// <c>liakontTime.getTimeZone</c> (identifiant IANA) et mémorise le résultat. Tout échec de résolution
/// (IANA inconnue, JS indisponible) retombe sur <see cref="TimeZoneInfo.Utc"/> plutôt que de lever — on
/// bloque l'exception, jamais le rendu (CLAUDE.md n°3, esprit).
/// </summary>
public sealed class BrowserTimeZone : IBrowserTimeZone
{
    private bool _resolved;

    /// <inheritdoc />
    public event Action? Resolved;

    /// <inheritdoc />
    public TimeZoneInfo? Zone { get; private set; }

    /// <inheritdoc />
    public bool IsResolved => _resolved;

    /// <summary>
    /// Mappe un identifiant IANA du navigateur vers un <see cref="TimeZoneInfo"/> (.NET résout les IDs IANA
    /// cross-plateforme). Identifiant absent/inconnu → <see cref="TimeZoneInfo.Utc"/> (jamais d'exception).
    /// </summary>
    public static TimeZoneInfo ResolveZone(string? ianaId)
    {
        if (string.IsNullOrWhiteSpace(ianaId))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException || ex is InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    /// <inheritdoc />
    public async Task EnsureResolvedAsync(IJSRuntime js, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(js);

        if (_resolved)
        {
            return;
        }

        string? ianaId;
        try
        {
            ianaId = await js.InvokeAsync<string?>("liakontTime.getTimeZone", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (
            ex is JSException
            || ex is JSDisconnectedException
            || ex is OperationCanceledException
            || ex is InvalidOperationException)
        {
            // JS indisponible (pré-rendu, circuit déconnecté/non interactif) : on NE marque PAS résolu — le
            // prochain OnAfterRenderAsync réessaiera. En attendant, Zone reste nul → affichage UTC explicite.
            return;
        }

        Zone = ResolveZone(ianaId);
        _resolved = true;
        Resolved?.Invoke();
    }
}
