namespace Liakont.SignatureProviders.Yousign.Wire;

/// <summary>Bloc <c>data</c> d'un événement de webhook Yousign v3 (type « fil » INTERNE — INV-YOUSIGN-2).</summary>
internal sealed record YousignWebhookData
{
    /// <summary>Demande de signature concernée par l'événement.</summary>
    public YousignSignatureRequestResponse? SignatureRequest { get; init; }
}
