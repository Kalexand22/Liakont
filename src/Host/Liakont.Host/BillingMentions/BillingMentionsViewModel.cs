namespace Liakont.Host.BillingMentions;

/// <summary>
/// Modèle assemblé de l'écran « Paramétrage › Mentions de facturation » (BUG-26, F12-A §3.4) : la saisie
/// éditable pré-remplie aux mentions actuelles du tenant (ou « non renseigné »). Aucune valeur par défaut
/// n'est appliquée : un champ vide signifie « mention non renseignée ».
/// </summary>
public sealed record BillingMentionsViewModel
{
    /// <summary>Valeurs éditables, pré-remplies aux mentions de facturation actuelles du tenant.</summary>
    public required BillingMentionsFormModel Form { get; init; }
}
