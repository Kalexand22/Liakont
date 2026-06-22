namespace Liakont.PaClients.ChorusPro;

/// <summary>
/// Environnement d'un compte Chorus Pro / PISTE (F18 §2.1/§3.3). N'aiguille AUCUNE URL en dur : la base
/// API et l'endpoint jeton sont portés par <see cref="ChorusProAccountConfig"/> (résolus par le Host /
/// paramétrage du tenant, F18 §3.3 « ne pas hardcoder »). Cet enum sert l'étiquetage (audit) et permet
/// au Host de choisir les bonnes URLs verrouillées au raccordement.
/// </summary>
public enum ChorusProEnvironment
{
    /// <summary>Qualification PISTE (hôtes préfixés <c>sandbox-</c> — F18 §2.1/§7).</summary>
    Qualification,

    /// <summary>Production PISTE (hôtes sans préfixe <c>sandbox-</c> — F18 §2.1/§7).</summary>
    Production,
}
