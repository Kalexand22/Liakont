// RB6 — résolution du fuseau horaire du NAVIGATEUR (et non du serveur Docker, qui tourne en UTC).
// JS propre à Liakont (le bundle socle Stratum.Common.UI/js/stratum-ui.js n'est pas modifié — règle n°11/20).
// Appelé une fois par circuit par le service scoped IBrowserTimeZone, puis le résultat est mémorisé côté C#.
window.liakontTime = window.liakontTime || {
    // Identifiant de fuseau IANA (ex. « Europe/Paris ») — TimeZoneInfo.FindSystemTimeZoneById le résout
    // (cross-plateforme depuis .NET 6 ; le serveur Docker tourne sous Linux). null/échec → UTC côté C#.
    getTimeZone: function () {
        try {
            return Intl.DateTimeFormat().resolvedOptions().timeZone;
        } catch (e) {
            return null;
        }
    }
};
