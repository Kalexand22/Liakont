namespace Liakont.Agent.Adapters.EncheresV6;

using System;

/// <summary>
/// Identité de l'émetteur (le vendeur / la SVV cliente) pour la source EncheresV6. Le SIREN de
/// l'émetteur n'est PAS dans la base EncheresV6 (champ libre, non fiable) : il vient du
/// PARAMÉTRAGE du tenant (<see cref="Liakont.Agent.Core.Extraction.EmitterIdentitySource.FromConfig"/>,
/// F01-F02 §4.3). Aucune donnée client n'est embarquée dans le code (CLAUDE.md n°7) : cette identité
/// est fournie par la configuration de l'agent (ADP04) ou, en test/démo, par une valeur fictive.
/// </summary>
public sealed class EncheresV6EmitterIdentity
{
    /// <summary>Crée une identité d'émetteur.</summary>
    /// <param name="name">Raison sociale de l'émetteur (EN 16931 BT-27). Obligatoire.</param>
    /// <param name="siren">SIREN de l'émetteur (EN 16931 BT-30, scheme 0002). Obligatoire (vient de la config).</param>
    /// <param name="siret">SIRET de l'émetteur (optionnel).</param>
    /// <param name="vatNumber">N° de TVA intracommunautaire de l'émetteur (optionnel).</param>
    /// <param name="city">Ville du siège (optionnel).</param>
    /// <param name="postalCode">Code postal du siège (optionnel).</param>
    /// <param name="street">Voie / ligne d'adresse du siège (optionnel).</param>
    /// <param name="countryCode">Code pays ISO 3166-1 alpha-2 (EN 16931 BT-40). Défaut « FR ».</param>
    public EncheresV6EmitterIdentity(
        string name,
        string siren,
        string? siret = null,
        string? vatNumber = null,
        string? city = null,
        string? postalCode = null,
        string? street = null,
        string countryCode = "FR")
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("La raison sociale de l'émetteur est requise (paramétrage tenant).", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(siren))
        {
            throw new ArgumentException("Le SIREN de l'émetteur est requis (paramétrage tenant — absent de la base EncheresV6).", nameof(siren));
        }

        Name = name;
        Siren = siren;
        Siret = siret;
        VatNumber = vatNumber;
        City = city;
        PostalCode = postalCode;
        Street = street;
        CountryCode = countryCode;
    }

    /// <summary>Raison sociale de l'émetteur (EN 16931 BT-27).</summary>
    public string Name { get; }

    /// <summary>SIREN de l'émetteur (EN 16931 BT-30, scheme 0002).</summary>
    public string Siren { get; }

    /// <summary>SIRET de l'émetteur (optionnel).</summary>
    public string? Siret { get; }

    /// <summary>N° de TVA intracommunautaire de l'émetteur (optionnel).</summary>
    public string? VatNumber { get; }

    /// <summary>Ville du siège (optionnel).</summary>
    public string? City { get; }

    /// <summary>Code postal du siège (optionnel).</summary>
    public string? PostalCode { get; }

    /// <summary>Voie / ligne d'adresse du siège (optionnel).</summary>
    public string? Street { get; }

    /// <summary>Code pays ISO 3166-1 alpha-2 (EN 16931 BT-40).</summary>
    public string CountryCode { get; }
}
