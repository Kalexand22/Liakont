namespace Liakont.SignatureProviders.Yousign;

/// <summary>
/// Environnement Yousign sélectionnable par un tenant (ADR-0029 §6). Le tenant ne choisit qu'ENTRE des
/// environnements CONNUS — jamais une adresse, jamais un host/path nu : l'URL de base est dérivée d'une
/// <see cref="YousignUrlAllowlist">allowlist d'origines HTTPS exactes</see> par environnement (anti-SSRF,
/// INV-YOUSIGN-7). Valeur par défaut <see cref="Sandbox"/> (le moins privilégié).
/// </summary>
public enum YousignEnvironment
{
    /// <summary>Bac à sable Yousign (API v3 sandbox) — défaut, niveaux réellement vérifiés.</summary>
    Sandbox = 0,

    /// <summary>Production Yousign (API v3) — activée au déploiement.</summary>
    Production = 1,
}
