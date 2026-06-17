namespace Liakont.OnSiteSignature.Client;

using System;
using System.Net;

/// <summary>
/// Point d'entrée de l'hôte capteur de signature sur place. Le câblage réel du pad (SDK Wacom Ink natif)
/// est fourni par l'installeur de déploiement derrière <see cref="ISignaturePadDevice"/> (README.md) :
/// par défaut, aucun pad n'est intégré (<see cref="NotConnectedSignaturePadDevice"/>). Ce point d'entrée ne
/// porte AUCUNE logique métier — la capture est orchestrée par <see cref="OnSiteSignatureSession"/> et
/// décidée côté plateforme.
/// </summary>
internal static class Program
{
    /// <summary>Démarre l'hôte capteur.</summary>
    /// <param name="args">Arguments de ligne de commande (non utilisés à ce stade).</param>
    /// <returns>Code de sortie du processus.</returns>
    internal static int Main(string[] args)
    {
        _ = args;

        // TLS 1.2+ obligatoire : net48 négocie des protocoles obsolètes par défaut, refusés par la plateforme.
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

        Console.WriteLine(
            "Hôte de signature sur place (Wacom) — capteur pur. Branchez le pad STU et installez l'hôte de "
            + "capture livré avec le SDK Wacom (README.md). La capture est ensuite transmise au proxy "
            + "OnSiteCapture de la plateforme, qui décide (binding, signataire vérifié, WORM).");
        return 0;
    }
}
