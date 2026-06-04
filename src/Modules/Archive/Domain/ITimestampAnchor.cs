namespace Liakont.Modules.Archive.Domain;

/// <summary>
/// Ancrage temporel de la TÊTE DE CHAÎNE du coffre d'un tenant (TRK06, F06 §3 « scellement renforcé »).
/// Axe enfichable à capacités, choisi par config d'INSTANCE (blueprint §2 règle 6). L'ancrage scelle un
/// instant : « ce <c>chain_hash</c> existait à T » — il renforce la chaîne de hashes (qui reste l'intégrité
/// de référence, indépendante du backend), il ne la remplace pas. Le module ne référence jamais un ancrage
/// concret : il ne voit que cette abstraction et ses <see cref="TimestampAnchorCapabilities"/>.
/// </summary>
public interface ITimestampAnchor
{
    /// <summary>Capacités déclarées de cet ancrage (pilotent le job et le vérifieur, jamais un test de type).</summary>
    TimestampAnchorCapabilities Capabilities { get; }

    /// <summary>
    /// Ancre l'empreinte de tête de chaîne <paramref name="chainHeadDigest"/> (les octets bruts du
    /// <c>chain_hash</c>, soit un condensé SHA-256 de 32 octets) auprès du service d'horodatage et renvoie
    /// la preuve. Un ancrage non opérationnel (capacité <see cref="TimestampAnchorCapabilities.IsOperational"/>
    /// à <c>false</c>) lève une <see cref="System.NotSupportedException"/> explicite (jamais d'ancrage silencieux).
    /// </summary>
    Task<TimestampAnchorResult> AnchorAsync(byte[] chainHeadDigest, CancellationToken cancellationToken = default);

    /// <summary>
    /// Vérifie qu'une preuve <paramref name="proof"/> horodate bien l'empreinte <paramref name="chainHeadDigest"/>
    /// (signature de la TSA valide et empreinte scellée == empreinte attendue). N'effectue AUCUN appel réseau.
    /// </summary>
    Task<TimestampVerification> VerifyAsync(byte[] proof, byte[] chainHeadDigest, CancellationToken cancellationToken = default);
}
