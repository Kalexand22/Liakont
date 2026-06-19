namespace Liakont.OnSiteSignature.Client;

using System;

/// <summary>
/// Pad par DÉFAUT quand aucun SDK Wacom natif n'est intégré (build/CI, ou poste sans pad). Toute tentative
/// de capture lève un message opérateur clair en français (CLAUDE.md n°12). L'hôte de déploiement substitue
/// l'implémentation réelle (SDK Wacom Ink) derrière <see cref="ISignaturePadDevice"/> (README.md).
/// </summary>
internal sealed class NotConnectedSignaturePadDevice : ISignaturePadDevice
{
    /// <inheritdoc />
    public CapturedSignature Capture() =>
        throw new InvalidOperationException(
            "Aucun pad de signature Wacom n'est connecté ou intégré. Branchez le pad STU et installez l'hôte "
            + "de capture livré avec le SDK Wacom (voir README.md du client de signature sur place).");
}
