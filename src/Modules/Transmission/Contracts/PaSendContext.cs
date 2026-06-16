namespace Liakont.Modules.Transmission.Contracts;

using System;

/// <summary>
/// Contexte d'envoi OPTIONNEL passé au plug-in PA par le pipeline à l'étape <c>Sending</c> (FX07, F16 §6.1).
/// Porte l'artefact fiscal PRÉ-CONSTRUIT par la plateforme (le Factur-X PDF/A-3 scellé) à transmettre TEL
/// QUEL. Les PA de niveau « Pilotage » (Super PDP, B2Brouter) l'IGNORENT — elles construisent leur propre
/// payload depuis le pivot ; la PA générique (niveau « Essentiel ») l'EXIGE : artefact absent → blocage,
/// jamais régénéré dans le plug-in (indépendance plug-in, CLAUDE.md n°6 ; « bloquer plutôt qu'envoyer
/// faux », n°3). C'est le contrat de passage de l'artefact référencé par F16 §6.1, scopé à FX07.
/// </summary>
public sealed class PaSendContext
{
    /// <summary>Construit un contexte d'envoi portant l'artefact pré-construit à transmettre.</summary>
    /// <param name="preBuiltArtifact">Octets de l'artefact fiscal scellé (Factur-X PDF/A-3). Vide → blocage côté plug-in.</param>
    public PaSendContext(ReadOnlyMemory<byte> preBuiltArtifact)
    {
        PreBuiltArtifact = preBuiltArtifact;
    }

    /// <summary>
    /// Octets de l'artefact fiscal pré-construit par la plateforme (Factur-X PDF/A-3 scellé), à transmettre
    /// tel quel. Vide quand aucun artefact n'a été généré (PA de niveau Pilotage) — la PA générique bloque
    /// alors plutôt que d'émettre à vide.
    /// </summary>
    public ReadOnlyMemory<byte> PreBuiltArtifact { get; }
}
