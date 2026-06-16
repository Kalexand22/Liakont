namespace Liakont.Modules.Signature.Contracts;

/// <summary>
/// COMMENT la complétion d'une signature est signalée (ADR-0027 §2 ; F17 §2). <c>[Flags]</c> COMBINABLE
/// et ORTHOGONAL à <see cref="SignatureMode"/> : un fournisseur peut déclarer <c>Webhook | Polling</c>
/// (webhook primaire + polling de réconciliation en secours), un distant <em>polling-only</em>
/// <c>Polling</c>, un capteur sur place <c>Synchronous</c>. <see cref="ISignatureProvider.HandleWebhookAsync"/>
/// n'est pertinent QUE si le flag <see cref="Webhook"/> est positionné (sinon résultat typé NotSupported —
/// INV-SIGPROV-3) ; le flag <see cref="Polling"/> autorise un job de réconciliation
/// <see cref="ISignatureProvider.GetSignatureStatusAsync"/>. Valeurs en puissances de deux distinctes,
/// <c>None = 0</c> (même garde anti-bug <c>[Flags]</c> que <see cref="SignatureMode"/>).
/// </summary>
[Flags]
public enum CompletionTransport
{
    /// <summary>Aucun transport déclaré.</summary>
    None = 0,

    /// <summary>Complétion SYNCHRONE : le résultat est connu au retour de la demande (capteur sur place).</summary>
    Synchronous = 1,

    /// <summary>Complétion par WEBHOOK : le fournisseur rappelle la plateforme (server-side, ex. Yousign).</summary>
    Webhook = 2,

    /// <summary>Complétion par POLLING : la plateforme interroge l'état (réconciliation de secours).</summary>
    Polling = 4,
}
