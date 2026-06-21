namespace Liakont.Modules.FacturX.Infrastructure;

using System;
using Microsoft.Extensions.Configuration;
using QuestPDF.Infrastructure;

/// <summary>
/// Résolution de la licence QuestPDF (RDF18, DEC-3 ; redline ADR-0023 / ADR socle ADR-0004). La licence
/// n'est PLUS codée en dur : elle est déclarée par TOPOLOGIE/INSTANCE via la configuration
/// (<c>QuestPdf:LicenseType</c>), et un contrôle au déploiement EXIGE une valeur explicite et reconnue —
/// fermé par défaut sinon (« bloquer plutôt qu'envoyer faux » : le scellement PDF/A-3 est sur le chemin
/// d'émission FISCALE, INV-FX-1).
/// <para>Le <see cref="LicenseType"/> retenu (Community gratuit &lt; 1 M$ de CA, Professional, Enterprise)
/// est une donnée de DÉPLOIEMENT, jamais une donnée client dans le code (CLAUDE.md n°7). La déclaration
/// du « licensee » par topologie (ambiguïté de marque grise) reste un geste de paramétrage/déploiement
/// (DEC-3, arbitrage opérateur) — ce résolveur n'exige qu'un <em>type</em> explicite, pas une clé.</para>
/// <para>Co-localisé dans <c>FacturX.Infrastructure</c> car il référence l'énumération
/// <see cref="LicenseType"/> de QuestPDF, confinée à cette couche (INV-FX-1) ; le Host ne référence
/// jamais QuestPDF.</para>
/// </summary>
public static class QuestPdfLicenseConfiguration
{
    /// <summary>Section de configuration portant le type de licence QuestPDF.</summary>
    public const string SectionName = "QuestPdf";

    /// <summary>Clé complète (section:propriété) du type de licence QuestPDF.</summary>
    public const string LicenseTypeKey = SectionName + ":LicenseType";

    /// <summary>
    /// Lit et valide le type de licence QuestPDF depuis la configuration. Fermé par défaut : valeur
    /// absente, vide ou non reconnue échoue le démarrage (message opérateur français).
    /// </summary>
    /// <param name="configuration">Configuration de l'application (topologie/instance).</param>
    /// <returns>Le <see cref="LicenseType"/> déclaré.</returns>
    /// <exception cref="InvalidOperationException">Si la valeur est absente, vide ou non reconnue.</exception>
    public static LicenseType Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return Resolve(configuration[LicenseTypeKey]);
    }

    /// <summary>
    /// Surcharge pure (testable sans <see cref="IConfiguration"/>) : valide une valeur littérale de licence.
    /// Seuls les NOMS d'énumération sont admis (pas une valeur numérique : un « 1 » opaque n'est pas une
    /// déclaration explicite de type).
    /// </summary>
    /// <param name="configuredValue">Valeur brute lue de la configuration (<c>QuestPdf:LicenseType</c>).</param>
    /// <returns>Le <see cref="LicenseType"/> correspondant.</returns>
    /// <exception cref="InvalidOperationException">Si la valeur est absente, vide ou non reconnue.</exception>
    public static LicenseType Resolve(string? configuredValue)
    {
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            throw new InvalidOperationException(
                $"Licence QuestPDF non déclarée : la clé de configuration « {LicenseTypeKey} » est absente "
                + "ou vide. Déclarez-la explicitement par topologie/instance (valeurs admises : Community, "
                + "Professional, Enterprise) avant de démarrer la plateforme.");
        }

        var trimmed = configuredValue.Trim();

        // Exiger un NOM EXACT (insensible à la casse) parmi les membres de l'énumération : seule une
        // déclaration explicite du type est acceptée. Enum.TryParse accepterait des valeurs numériques
        // (« 0 », « 1 ») et des listes virgule-séparées (« Community,Enterprise ») qui ne constituent
        // pas une déclaration explicite de type — ces cas sont rejetés ici.
        foreach (var name in Enum.GetNames<LicenseType>())
        {
            if (string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return Enum.Parse<LicenseType>(name);
            }
        }

        throw new InvalidOperationException(
            $"Licence QuestPDF invalide : « {trimmed} » (clé « {LicenseTypeKey} »). Valeurs admises : "
            + "Community, Professional, Enterprise. Corrigez la configuration de déploiement avant de "
            + "démarrer la plateforme.");
    }
}
