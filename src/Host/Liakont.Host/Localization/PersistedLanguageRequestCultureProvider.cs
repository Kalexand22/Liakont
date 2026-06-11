namespace Liakont.Host.Localization;

using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Logging;
using Stratum.Modules.Identity.Application.Preferences;

/// <summary>
/// Fournit la culture de la requête depuis la préférence Language PERSISTÉE de l'utilisateur
/// authentifié (identity.user_preferences) — décision opérateur 2026-06-10 (bug-inbox console-web) :
/// la base est la source de vérité de la langue ; le cookie <c>.AspNetCore.Culture</c> n'est qu'un
/// repli pour les requêtes anonymes (ex. /login) — il est par hôte et peut être périmé. Doit être
/// enregistré AVANT le provider cookie, et le middleware de localisation doit tourner APRÈS
/// l'authentification (le provider lit les claims du principal).
/// </summary>
internal sealed partial class PersistedLanguageRequestCultureProvider : RequestCultureProvider
{
    public override async Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var user = httpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            // Anonyme : on laisse la main aux providers suivants (cookie), puis à la culture par défaut.
            return null;
        }

        var raw = user.FindFirst("stratum_user_id")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(raw, out var userId))
        {
            return null;
        }

        var cache = httpContext.RequestServices.GetRequiredService<UserCultureCache>();
        if (!cache.TryGet(userId, out var language))
        {
            try
            {
                var preferences = httpContext.RequestServices.GetRequiredService<IUserPreferencesService>();
                var stored = await preferences.GetAsync(userId, httpContext.RequestAborted);
                language = stored?.Language;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // La résolution de langue ne doit JAMAIS faire échouer la requête : repli cookie/défaut,
                // échec tracé (pas de cache de l'échec : nouvel essai à la prochaine requête).
                LogPreferenceReadFailed(
                    httpContext.RequestServices.GetRequiredService<ILogger<PersistedLanguageRequestCultureProvider>>(),
                    ex,
                    userId);
                return null;
            }

            // L'absence de préférence est aussi mémorisée : pas de lecture base par requête pour autant.
            cache.Set(userId, language);
        }

        var culture = ResolveSupported(language);
        return culture is null ? null : new ProviderCultureResult(culture);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lecture de la préférence de langue impossible pour l'utilisateur {UserId}.")]
    private static partial void LogPreferenceReadFailed(ILogger logger, Exception exception, Guid userId);

    private static string? ResolveSupported(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        if (SupportedCultures.IsSupported(language))
        {
            return language;
        }

        // Valeur historique possible « fr-FR » : retomber sur la culture neutre supportée.
        var dash = language.IndexOf('-', StringComparison.Ordinal);
        if (dash > 0)
        {
            var neutral = language[..dash];
            if (SupportedCultures.IsSupported(neutral))
            {
                return neutral;
            }
        }

        return null;
    }
}
