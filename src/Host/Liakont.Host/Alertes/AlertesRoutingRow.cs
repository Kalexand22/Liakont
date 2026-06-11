namespace Liakont.Host.Alertes;

/// <summary>Une ligne éditable de la matrice de routage : cible (sélecteur encodé) + destinataires en clair (CSV).</summary>
public sealed class AlertesRoutingRow
{
    /// <summary>Cible encodée (<c>rule:&lt;clé&gt;</c> ou <c>severity:&lt;gravité&gt;</c>), vide tant que non choisie.</summary>
    public string Selector { get; set; } = string.Empty;

    /// <summary>Destinataires e-mail séparés par virgule ou point-virgule.</summary>
    public string RecipientsCsv { get; set; } = string.Empty;
}
