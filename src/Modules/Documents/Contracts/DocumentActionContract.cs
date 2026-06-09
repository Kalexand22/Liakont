namespace Liakont.Modules.Documents.Contracts;

/// <summary>
/// Identifiants STABLES et PARTAGÉS des actions opérateur du garde-fou B2B/B2C et de la re-vérification
/// (F08 §A.4, items API02b / WEB03b) : valeurs canoniques du verdict (corps de requête HTTP), codes d'audit
/// (piste d'activité), états du document concernés et motif structuré du traitement manuel. SOURCE UNIQUE
/// consommée à la fois par l'endpoint HTTP (<c>DocumentActionsEndpointMapping</c>) et par le service
/// in-process de la console (<c>DocumentControlActionsService</c>) — ces deux canaux exécutent le MÊME geste
/// opérateur (le cookie OIDC n'est pas disponible dans le circuit Blazor, d'où l'appel in-process), donc leur
/// piste d'audit DOIT être identique : centraliser ces identifiants ici empêche toute divergence silencieuse
/// (CLAUDE.md n°4 — fidélité de la piste d'audit).
/// </summary>
public static class DocumentActionContract
{
    /// <summary>État d'un document bloqué : seul état où le verdict / la re-vérification s'appliquent (DocumentState, exposé en chaîne).</summary>
    public const string BlockedState = "Blocked";

    /// <summary>État « prêt à l'envoi » (DocumentState, exposé en chaîne) : seul état envoyable (actions d'envoi WEB05 / API02a).</summary>
    public const string ReadyToSendState = "ReadyToSend";

    /// <summary>État terminal d'un document traité manuellement hors passerelle (DocumentState, exposé en chaîne).</summary>
    public const string ManuallyHandledState = "ManuallyHandled";

    /// <summary>Verdict « confirmer particulier (B2C) » du garde-fou B2B/B2C (valeur canonique du corps de requête).</summary>
    public const string VerdictConfirmB2c = "confirm_b2c";

    /// <summary>Verdict « traiter manuellement hors passerelle (B2B) » du garde-fou B2B/B2C (valeur canonique du corps de requête).</summary>
    public const string VerdictHandleManually = "handle_manually";

    /// <summary>Type d'entité de la piste d'audit pour un document.</summary>
    public const string DocumentEntityType = "Document";

    /// <summary>Code d'audit du verdict « confirmer particulier (B2C) ».</summary>
    public const string VerdictConfirmB2cActivity = "documents.verdict_confirm_b2c";

    /// <summary>Code d'audit du verdict « traiter manuellement (B2B) ».</summary>
    public const string VerdictHandleManuallyActivity = "documents.verdict_handle_manually";

    /// <summary>Code d'audit de la re-vérification d'un document bloqué.</summary>
    public const string RecheckActivity = "documents.rechecked";

    /// <summary>Code d'audit de l'envoi déclenché depuis un document (action « Envoyer » / « Envoyer la sélection » — API02a / WEB05).</summary>
    public const string SendTriggeredActivity = "documents.send_triggered";

    /// <summary>Code d'audit de l'envoi groupé « Tout envoyer » du tenant (API02a / WEB05).</summary>
    public const string SendAllTriggeredActivity = "documents.send_all_triggered";

    /// <summary>Type d'entité de la piste d'audit de l'envoi groupé (non rattaché à un document unique).</summary>
    public const string SendAllEntityType = "Documents";

    /// <summary>Identifiant d'entité de la piste d'audit de l'envoi groupé « Tout envoyer ».</summary>
    public const string SendAllEntityId = "send-all";

    /// <summary>Motif journalisé du traitement manuel B2B issu du garde-fou (verdict structuré — l'opérateur ne saisit pas de texte ici, F08 §A.4).</summary>
    public const string ManualB2bReason =
        "Garde-fou B2B/B2C : acheteur professionnel — facture B2B traitée manuellement hors passerelle (verdict opérateur, F08 §A.4).";
}
