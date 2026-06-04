namespace Liakont.Modules.Archive.Domain;

/// <summary>
/// Capacités DÉCLARÉES d'un ancrage temporel (TRK06). Le consommateur (job d'ancrage, vérifieur) pilote
/// son comportement par ces capacités — JAMAIS par un test de type concret (<c>if (anchor is Rfc3161...)</c>
/// interdit, même discipline que <see cref="ArchiveStoreCapabilities"/> et les plug-ins PA, CLAUDE.md n°8).
/// </summary>
/// <param name="Method">La méthode d'ancrage (pour étiqueter la preuve, jamais pour brancher du code).</param>
/// <param name="IsOperational">
/// <c>false</c> pour un ancrage présent mais non disponible en V1 (ex. OpenTimestamps reporté en V1.1,
/// ADR-0011) : l'enregistrement et le job le signalent au lieu de l'utiliser silencieusement (pas de faux vert).
/// </param>
/// <param name="ProducesImmediateProof">
/// <c>true</c> si <see cref="ITimestampAnchor.AnchorAsync"/> renvoie une preuve COMPLÈTE et vérifiable
/// immédiatement (RFC 3161). <c>false</c> pour un ancrage asynchrone (cycle pending → complete) ou sans preuve.
/// </param>
/// <param name="RequiresOutboundInternet">L'ancrage appelle un service externe (TSA, calendar) — refusé sur une instance air-gapped.</param>
public readonly record struct TimestampAnchorCapabilities(
    TimestampAnchorMethod Method,
    bool IsOperational,
    bool ProducesImmediateProof,
    bool RequiresOutboundInternet);
