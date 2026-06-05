namespace Liakont.PaClients.B2Brouter;

/// <summary>
/// Environnement d'un compte B2Brouter (F05 §2 : staging vs production). Détermine l'URL de base.
/// Renseigné par le paramétrage du tenant (CFG02 : <c>AddPaAccountCommand.Environment</c>),
/// jamais deviné par le code.
/// </summary>
public enum B2BrouterEnvironment
{
    /// <summary>Staging — <c>https://api-staging.b2brouter.net</c> (F05 §2).</summary>
    Staging = 0,

    /// <summary>Production — <c>https://api.b2brouter.net</c> (F05 §2).</summary>
    Production = 1,
}
