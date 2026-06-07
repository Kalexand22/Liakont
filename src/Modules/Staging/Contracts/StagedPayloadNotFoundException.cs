namespace Liakont.Modules.Staging.Contracts;

using System;

/// <summary>
/// Levée par <see cref="IPayloadStagingStore.ReadAsync"/> quand aucune entrée n'existe pour la clé
/// demandée. Sémantique imposée par ADR-0014 §3 : une entrée ABSENTE est <b>transitoire</b> (« pas encore
/// stagé / à re-tenter »), <b>jamais</b> un état terminal ni une perte. Le consommateur (pipeline, PIP01)
/// doit la traiter comme un retry, jamais comme un échec définitif.
/// </summary>
public sealed class StagedPayloadNotFoundException : Exception
{
    private StagedPayloadNotFoundException(string message)
        : base(message)
    {
    }

    /// <summary>Construit l'exception pour une clé absente (transitoire / à re-tenter).</summary>
    /// <param name="key">La clé dont aucune entrée n'a été trouvée.</param>
    /// <returns>L'exception décrivant l'absence transitoire.</returns>
    public static StagedPayloadNotFoundException ForKey(StagedPayloadKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new StagedPayloadNotFoundException(
            $"Aucun contenu stagé pour le document {key.DocumentId} (tenant {key.TenantId}) : non encore stagé, à re-tenter (transitoire, jamais terminal).");
    }
}
