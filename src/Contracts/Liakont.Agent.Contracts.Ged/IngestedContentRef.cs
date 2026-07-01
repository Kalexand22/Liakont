namespace Liakont.Agent.Contracts.Ged;

/// <summary>
/// Référence au contenu binaire d'un document ingéré par le canal GED (F19 §4.2) — DTO PUR. L'agent
/// DÉCRIT le binaire (emplacement logique côté source, type MIME, taille, empreinte) sans jamais
/// l'interpréter (CLAUDE.md n°6) ; <c>null</c> sur <see cref="IngestedDocumentDto.Content"/> quand la
/// source ne porte aucun binaire. Le rangement write-once probant vit sur la PLATEFORME (§4.3.2 / §5.1),
/// jamais côté agent.
/// </summary>
public sealed class IngestedContentRef
{
    /// <summary>Crée une référence de contenu binaire.</summary>
    /// <param name="contentRef">Référence logique du binaire dans la source (chemin/clé BRUT, jamais interprété).</param>
    /// <param name="mediaType">Type MIME BRUT déclaré par la source (ex. « application/pdf »).</param>
    /// <param name="byteLength">Taille du binaire en octets, telle que rapportée par la source.</param>
    /// <param name="contentHash">Empreinte du binaire (hex), pour la détection d'altération et l'indexation (§3.4.1).</param>
    public IngestedContentRef(string contentRef, string mediaType, long byteLength, string contentHash)
    {
        ContentRef = contentRef;
        MediaType = mediaType;
        ByteLength = byteLength;
        ContentHash = contentHash;
    }

    /// <summary>Référence logique du binaire dans la source (BRUTE).</summary>
    public string ContentRef { get; }

    /// <summary>Type MIME BRUT déclaré par la source.</summary>
    public string MediaType { get; }

    /// <summary>Taille du binaire en octets.</summary>
    public long ByteLength { get; }

    /// <summary>Empreinte du binaire (hex).</summary>
    public string ContentHash { get; }
}
