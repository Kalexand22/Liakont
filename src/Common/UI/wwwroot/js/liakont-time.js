// Liakont addition (affichage des dates cote navigateur) - not part of the original Stratum vendoring.
// RB6 — résolution du fuseau horaire du NAVIGATEUR (et non du serveur Docker, qui tourne en UTC).
// Servi par le RCL Stratum.Common.UI (_content/Stratum.Common.UI/js/liakont-time.js), consommé par le
// service IBrowserTimeZone (Host + pages d'admin socle). Appelé une fois par circuit, résultat mémorisé en C#.
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
