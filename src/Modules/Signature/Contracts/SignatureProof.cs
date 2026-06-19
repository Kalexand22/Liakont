namespace Liakont.Modules.Signature.Contracts;

/// <summary>
/// Preuve de signature téléchargée d'un fournisseur (ADR-0027 §2), destinée au rapatriement WORM via
/// <c>Archive.Contracts</c> (jamais par le plug-in lui-même — SIG07). Calqué sur <c>PaGeneratedDocument</c> :
/// une capacité absente porte <see cref="CapabilityNotSupported"/> et un contenu nul — jamais une exception
/// (INV-SIGPROV-5).
/// </summary>
public sealed record SignatureProof
{
    /// <summary>Octets de la preuve (PDF signé / dossier de preuve), ou <c>null</c> si indisponible.</summary>
    public IReadOnlyList<byte>? Content { get; init; }

    /// <summary>Type de média de la preuve (ex. « application/pdf »), ou <c>null</c>.</summary>
    public string? ContentType { get; init; }

    /// <summary>Détail de la capacité absente si la preuve ne peut être fournie ; <c>null</c> sinon.</summary>
    public SignatureCapabilityNotSupportedResult? CapabilityNotSupported { get; init; }

    /// <summary>Construit une preuve disponible.</summary>
    /// <param name="content">Octets de la preuve.</param>
    /// <param name="contentType">Type de média (facultatif).</param>
    public static SignatureProof Available(IReadOnlyList<byte> content, string? contentType = null) => new()
    {
        Content = content,
        ContentType = contentType,
    };

    /// <summary>Construit un résultat « preuve indisponible : capacité absente » (jamais d'exception).</summary>
    /// <param name="capabilityGap">Détail journalisable de la capacité manquante.</param>
    public static SignatureProof NotSupported(SignatureCapabilityNotSupportedResult capabilityGap) => new()
    {
        CapabilityNotSupported = capabilityGap,
    };
}
