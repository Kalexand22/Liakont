namespace Liakont.PaClients.SuperPdp;

/// <summary>
/// Environnement d'un compte Super PDP (F14 §3.1 : sandbox vs production). Détermine l'URL de base.
/// Renseigné par le paramétrage du tenant (CFG02 : <c>AddPaAccountCommand.Environment</c>), jamais
/// deviné par le code (CLAUDE.md n°2/7).
/// </summary>
public enum SuperPdpEnvironment
{
    /// <summary>Sandbox — base d'API de recette (F14 §3.1, point ouvert O1 confirmé en sandbox PAS03).</summary>
    Sandbox = 0,

    /// <summary>Production (F14 §3.1, base à confirmer sandbox/quick-start — O1).</summary>
    Production = 1,
}
