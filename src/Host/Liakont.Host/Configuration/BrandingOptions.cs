namespace Liakont.Host.Configuration;

using System;

/// <summary>
/// Branding de NIVEAU INSTANCE — « marque grise » (blueprint.md §3.3, F12 §6.1) — lié depuis la section
/// <c>Branding</c> des appsettings de l'instance. Une instance de plateforme = un éditeur : il vend la
/// plateforme sous SA marque, et ses clients ne voient « Liakont »/« IT Innovations » que s'il le veut.
/// Les valeurs par défaut sont la marque « Liakont » (l'instance mutualisée IT Innovations les utilise
/// telles quelles). AUCUNE donnée client n'est ici : ce sont des PARAMÈTRES d'instance (CLAUDE.md n°7),
/// le code n'embarque que la marque produit par défaut. La même section est lue, pour leur seule tranche,
/// par les emails (transport SMTP) et l'export de réversibilité (Archive).
/// </summary>
public sealed class BrandingOptions
{
    public const string SectionName = "Branding";

    /// <summary>Marque produit par défaut, utilisée quand l'instance ne configure rien.</summary>
    public const string DefaultCommercialName = "Liakont";

    /// <summary>Nom commercial affiché (coquille, titre d'onglet navigateur, marque de la barre latérale).</summary>
    public string CommercialName { get; init; } = DefaultCommercialName;

    /// <summary>URL / chemin du logo affiché dans la coquille. Vide = pas de logo (texte de marque seul).</summary>
    public string LogoUrl { get; init; } = string.Empty;

    /// <summary>URL / chemin du favicon. Vide = aucun favicon dédié.</summary>
    public string FaviconUrl { get; init; } = string.Empty;

    /// <summary>
    /// Couleur primaire de marque (hex CSS, ex. <c>#001b44</c>). Vide ou invalide = thème socle par défaut.
    /// CONTRAT : doit être une couleur SOMBRE (comme le bleu marine par défaut) — la barre latérale en dérive
    /// (<c>--color-primary-600/700</c> en clair, <c>--sidebar-bg/-header</c> en sombre) et y affiche du texte
    /// CLAIR (<c>--sidebar-text</c>) ; une primaire claire rendrait ce texte illisible. La surcharge couvre
    /// les DEUX thèmes (RB5) : barre latérale + CTA en clair, barre latérale en sombre (où le socle code
    /// sinon un fond en dur) — un rendu cohérent quel que soit le thème.
    /// </summary>
    public string PrimaryColor { get; init; } = string.Empty;

    /// <summary>
    /// Couleur d'accent de marque (hex CSS). Vide ou invalide = pas de surcharge d'accent. Peint, dans les
    /// DEUX thèmes (RB5), <c>--color-primary-container</c> (hover/secondaire) ET <c>--sidebar-active-accent</c>
    /// (accent de la ligne SÉLECTIONNÉE de la barre latérale — le signal de marque le plus visible).
    /// </summary>
    public string AccentColor { get; init; } = string.Empty;

    /// <summary>Domaine public de l'instance (informatif). Vide = non renseigné.</summary>
    public string PublicDomain { get; init; } = string.Empty;

    /// <summary>Mention de pied de page (coquille et emails). Vide = aucune.</summary>
    public string FooterText { get; init; } = string.Empty;

    /// <summary>Nom d'expéditeur des emails. Vide = repli sur <c>Smtp:FromName</c>.</summary>
    public string EmailFromName { get; init; } = string.Empty;

    /// <summary>Adresse d'expéditeur des emails. Vide = repli sur <c>Smtp:FromAddress</c>.</summary>
    public string EmailFromAddress { get; init; } = string.Empty;

    /// <summary>
    /// Affiche la mention technique discrète « propulsé par Liakont » (coquille, emails, exports). Défaut
    /// vrai ; l'éditeur la met à faux pour masquer toute mention Liakont/IT Innovations (blueprint.md §3.3).
    /// </summary>
    public bool PoweredByLiakont { get; init; } = true;

    /// <summary>
    /// Nom commercial EFFECTIF : repli sur la marque produit par défaut (« Liakont ») quand
    /// <see cref="CommercialName"/> est vide ou blanc — un opérateur qui VIDE la clé en appsettings lie une
    /// chaîne vide PAR-DESSUS le défaut C#. Source UNIQUE du repli pour tous les consommateurs UI et email,
    /// de sorte que coquille, login, emails et notice d'export affichent tous la même marque (jamais un
    /// titre/marque blanc d'un côté et « Liakont » de l'autre).
    /// </summary>
    public string EffectiveCommercialName =>
        string.IsNullOrWhiteSpace(CommercialName) ? DefaultCommercialName : CommercialName;

    /// <summary>
    /// Valide qu'une chaîne est une couleur hex CSS (<c>#</c> suivi de 3, 4, 6 ou 8 chiffres hexadécimaux).
    /// Garde-fou avant injection dans un bloc <c>&lt;style&gt;</c> : une valeur de config malformée est
    /// ignorée (jamais émise telle quelle, pas d'évasion de balise possible).
    /// </summary>
    public static bool IsHexColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value[0] != '#')
        {
            return false;
        }

        int digits = value.Length - 1;
        if (digits != 3 && digits != 4 && digits != 6 && digits != 8)
        {
            return false;
        }

        for (int i = 1; i < value.Length; i++)
        {
            if (!Uri.IsHexDigit(value[i]))
            {
                return false;
            }
        }

        return true;
    }
}
