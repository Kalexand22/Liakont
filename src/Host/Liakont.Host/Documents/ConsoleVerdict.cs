namespace Liakont.Host.Documents;

/// <summary>Branche du verdict du garde-fou B2B/B2C (F08 §A.4) choisie par l'opérateur dans l'onglet Contrôles (WEB03b).</summary>
internal enum ConsoleVerdict
{
    /// <summary>« Confirmer particulier (B2C) » : acheteur confirmé particulier malgré l'indice professionnel.</summary>
    ConfirmIndividualB2c,

    /// <summary>« Traiter manuellement (B2B) » : facture professionnelle traitée hors passerelle (terminal).</summary>
    HandleManuallyB2b,
}
