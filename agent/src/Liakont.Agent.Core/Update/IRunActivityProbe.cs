namespace Liakont.Agent.Core.Update;

/// <summary>
/// Indique si un RUN d'extraction est en cours sur ce poste. Permet au coordinateur d'auto-update de
/// DIFFÉRER une mise à jour tant qu'un run tourne (F12 §2.5 garde-fou : jamais d'update pendant un run).
/// Couture testable : un test injecte un état de run déterministe.
/// </summary>
public interface IRunActivityProbe
{
    /// <summary>Vrai si un run d'extraction est (peut-être) en cours — au doute, répondre « oui » (sûr).</summary>
    bool IsRunInProgress();
}
