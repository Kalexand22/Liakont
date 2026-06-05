namespace Liakont.Agent.Core.Extraction;

/// <summary>
/// Origine de l'identité de l'émetteur (SIREN/numéro de TVA) pour la source (capacité déclarée —
/// ADR-0004 D2). Déclaratif : la normalisation éventuelle (n° TVA → SIREN) reste une affaire de
/// l'adaptateur/plateforme, jamais de l'agent générique.
/// </summary>
public enum EmitterIdentitySource
{
    /// <summary>L'identité de l'émetteur est présente dans la base source.</summary>
    InBase = 1,

    /// <summary>L'identité de l'émetteur vient de la configuration (absente de la base — cas EncheresV6).</summary>
    FromConfig = 2,

    /// <summary>L'identité de l'émetteur est dérivée de son numéro de TVA.</summary>
    DerivedFromVatNumber = 3,
}
