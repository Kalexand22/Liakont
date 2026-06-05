namespace Liakont.Agent.Core.Configuration;

using System.Collections.Generic;

/// <summary>
/// Section <c>extraction</c> de la configuration de l'agent (F12 §2.4). L'adaptateur source, la
/// planification locale et les chemins ; la chaîne ODBC reste sous forme PROTÉGÉE (DPAPI) — elle
/// n'est déchiffrée qu'au moment de l'usage via <see cref="Security.ISecretProtector"/>.
/// </summary>
public sealed class ExtractionConfig
{
    public ExtractionConfig(
        string adapter,
        string? odbcConnectionStringProtected,
        string? pdfPoolPath,
        IReadOnlyList<string> schedule,
        bool catchUpOnStart,
        string? fixturesPath = null,
        int? defaultPeriodDays = null)
    {
        Adapter = adapter;
        OdbcConnectionStringProtected = odbcConnectionStringProtected;
        PdfPoolPath = pdfPoolPath;
        Schedule = schedule;
        CatchUpOnStart = catchUpOnStart;
        FixturesPath = fixturesPath;
        DefaultPeriodDays = defaultPeriodDays;
    }

    /// <summary>Identifiant de l'adaptateur source (ex. « EncheresV6 », « Fixture »).</summary>
    public string Adapter { get; }

    /// <summary>Chaîne de connexion ODBC, chiffrée DPAPI (peut être absente : certaines sources n'ont pas d'ODBC).</summary>
    public string? OdbcConnectionStringProtected { get; }

    /// <summary>Dossier des PDF en vrac (pool non lié), optionnel selon les capacités de l'adaptateur.</summary>
    public string? PdfPoolPath { get; }

    /// <summary>Heures de déclenchement locales au format <c>HH:mm</c> (ex. <c>["03:00"]</c>).</summary>
    public IReadOnlyList<string> Schedule { get; }

    /// <summary>Rattrapage au démarrage du service si un run planifié a été manqué.</summary>
    public bool CatchUpOnStart { get; }

    /// <summary>
    /// Chemin (fichier ou répertoire) des fixtures JSON pour un adaptateur en mode dev/démo, optionnel.
    /// L'adaptateur qui le sait l'interprète (ex. EncheresV6 : chaîne ODBC OU chemin fixtures requis) ;
    /// le chargeur ne fait que le transporter.
    /// </summary>
    public string? FixturesPath { get; }

    /// <summary>
    /// Fenêtre d'extraction par défaut, en jours, paramétrage OPTIONNEL (jamais une profondeur inventée :
    /// la fenêtre de service reste pilotée par le filigrane et la plateforme — CLAUDE.md n°2). Mise à
    /// disposition de l'adaptateur/hôte pour un run manuel ou une première extraction. <c>null</c> si absente.
    /// </summary>
    public int? DefaultPeriodDays { get; }
}
