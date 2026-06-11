namespace Liakont.Modules.Pipeline.Contracts;

using System;
using System.Globalization;

/// <summary>
/// Période année-mois (<c>"yyyy-MM"</c>) utilisée comme filtre de DATE sur le jour d'encaissement des
/// agrégats de paiement (<c>GET /payments</c>, API01b). C'est un BORNAGE DE DATE pur, jamais une règle
/// fiscale : la qualification fiscale (transmissible / suspendu / non requis / capacité en attente) reste
/// portée par le statut de l'agrégat, calculé par PIP03a — exposé tel quel, jamais redérivé ici (CLAUDE.md n°2).
/// </summary>
public static class MonthPeriod
{
    /// <summary>
    /// Analyse une période <c>"yyyy-MM"</c> en bornes de jour <c>[start, endExclusive)</c> (premier jour du
    /// mois inclus, premier jour du mois suivant exclu). Renvoie <c>false</c> si <paramref name="period"/>
    /// est vide ou mal formée — l'appelant décide alors (filtre ignoré côté lecture, <c>400</c> côté endpoint).
    /// </summary>
    public static bool TryParse(string? period, out DateOnly start, out DateOnly endExclusive)
    {
        start = default;
        endExclusive = default;

        if (string.IsNullOrWhiteSpace(period))
        {
            return false;
        }

        if (!DateOnly.TryParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first))
        {
            return false;
        }

        start = first;
        endExclusive = first.AddMonths(1);
        return true;
    }
}
