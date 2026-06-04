namespace Liakont.Modules.Validation.Contracts.CreditNotes;

/// <summary>
/// État, du point de vue de la plateforme, de la facture d'origine référencée par un avoir
/// (F07-F08 §B.5). Renvoyé par <see cref="IIssuedInvoiceLookup"/> pour permettre à la règle des
/// avoirs (VAL04) de trancher : un avoir n'est transmissible que si sa facture d'origine est CONNUE
/// de la plateforme ET DÉJÀ ÉMISE — sinon il est bloqué (jamais de référence fabriquée, CLAUDE.md n°2).
/// </summary>
public enum OriginalInvoiceStatus
{
    /// <summary>
    /// Facture d'origine inconnue de la plateforme (jamais reçue/émise par elle) : avoir ORPHELIN.
    /// Valeur par défaut volontaire (« bloquer plutôt qu'envoyer faux », CLAUDE.md n°3) : un lookup
    /// non concluant doit conduire au blocage, pas à un envoi à l'aveugle (F07-F08 §B.4).
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Facture d'origine connue de la plateforme mais PAS encore émise : l'original doit être émis
    /// d'abord (ordre chronologique, F07-F08 §B.5) — l'avoir reste bloqué en attendant.
    /// </summary>
    KnownNotIssued = 1,

    /// <summary>
    /// Facture d'origine connue de la plateforme ET déjà émise : l'avoir peut référencer un original
    /// régulier (F07-F08 §B.5).
    /// </summary>
    KnownIssued = 2,
}
