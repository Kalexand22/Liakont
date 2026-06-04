namespace Liakont.Modules.Archive.Application;

/// <summary>États d'une ligne d'ancrage (<c>documents.archive_anchors</c>, TRK06).</summary>
public static class ArchiveAnchorStatus
{
    /// <summary>Ancrage complet et vérifiable (RFC 3161).</summary>
    public const string Anchored = "anchored";

    /// <summary>Ancrage en attente de complétion — cycle OpenTimestamps, réservé V1.1 (ADR-0011).</summary>
    public const string Pending = "pending";
}
