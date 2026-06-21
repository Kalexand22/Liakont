// Liakont addition (affichage des dates cote navigateur) - not part of the original Stratum vendoring.
namespace Stratum.Common.UI.Time;

using System;
using System.Globalization;

/// <summary>
/// Formatage COMMUN des dates/heures de la console (RB6) : convertit un instant UTC
/// (<see cref="DateTimeOffset"/>) vers le fuseau du NAVIGATEUR de l'opérateur, en français. Centralise la
/// culture fr-FR et la règle de repli. Synchrone (le rendu Razor l'est) ; le fuseau est fourni par
/// <see cref="IBrowserTimeZone.Zone"/>.
/// <para>RÈGLE DE REPLI (anti-mensonge) : tant que le fuseau navigateur n'est pas résolu (<paramref name="zone"/>
/// nul, pré-rendu), on n'affiche JAMAIS une heure locale ambiguë — on rend l'UTC EXPLICITEMENT suffixé. Une
/// fois le fuseau résolu, le composant se re-rend et l'heure passe en local (sans suffixe).</para>
/// </summary>
public static class LiakontDateDisplay
{
    /// <summary>Texte affiché pour une valeur absente.</summary>
    public const string Placeholder = "—";

    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Date + heure (défaut <c>dd/MM/yyyy HH:mm</c>) au fuseau navigateur, ou UTC suffixé si non résolu.</summary>
    public static string DateTime(DateTimeOffset? value, TimeZoneInfo? zone, string format = "dd/MM/yyyy HH:mm") =>
        Format(value, zone, format);

    /// <summary>Date seule (défaut <c>dd/MM/yyyy</c>) au fuseau navigateur, ou UTC suffixé si non résolu.</summary>
    public static string Date(DateTimeOffset? value, TimeZoneInfo? zone, string format = "dd/MM/yyyy") =>
        Format(value, zone, format);

    private static string Format(DateTimeOffset? value, TimeZoneInfo? zone, string format)
    {
        if (value is null)
        {
            return Placeholder;
        }

        if (zone is null)
        {
            // Fuseau navigateur encore inconnu : UTC EXPLICITE (jamais une heure locale fausse).
            return value.Value.ToUniversalTime().ToString(format + " 'UTC'", Fr);
        }

        DateTimeOffset local = TimeZoneInfo.ConvertTime(value.Value, zone);
        return local.ToString(format, Fr);
    }
}
