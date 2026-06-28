namespace Liakont.Modules.Validation.Domain.Identity;

using System;
using System.Collections.Generic;

/// <summary>
/// Validation d'un SIREN (F04 §3.1 / §4.1) : 9 chiffres satisfaisant la clé de Luhn.
/// Dérogations documentées (liste fermée) : le SIREN de La Poste (<see cref="LaPosteSiren"/>, F04 §4.1)
/// et les SIREN de TEST des sandboxes PA (<see cref="PaSandboxTestSirens"/>) — aucune règle inventée.
/// </summary>
/// <remarks>
/// Validateur élémentaire du lot VAL (item VAL02), destiné à remplacer la copie temporaire de CFG02
/// (<c>TenantSettings.Domain.Services.SirenValidator</c>). La consolidation est hors périmètre VAL02 :
/// la frontière inter-modules interdit à TenantSettings de référencer <c>Validation.Domain</c>, elle
/// passera par une brique partagée. Les deux implémentations restent équivalentes en comportement :
/// le SIREN de La Poste (356000000) satisfait DÉJÀ la clé de Luhn standard, donc la dérogation
/// explicite ci-dessous (F04 §4.1) ne crée AUCUNE divergence avec la copie CFG02 — elle rend
/// simplement l'autorisation de la spec explicite et traçable.
/// </remarks>
public static class SirenValidator
{
    /// <summary>
    /// SIREN de La Poste, autorisé par dérogation documentée (F04 §4.1). Constante exposée pour que
    /// le paramétrage et les tests référencent la même valeur de source.
    /// </summary>
    public const string LaPosteSiren = "356000000";

    /// <summary>
    /// SIREN de TEST des sandboxes PA, autorisés par dérogation de RECETTE (Karl, 27/06/2026) : un
    /// destinataire/émetteur ADRESSABLE du sandbox PA (SuperPDP « Tricatel » 000000001, « Burger Queen »
    /// 000000002) n'est PAS un SIREN réel et ne satisfait PAS la clé de Luhn ; on l'autorise EXPLICITEMENT
    /// (liste fermée, même mécanique que <see cref="LaPosteSiren"/>) pour exercer le pipeline e-invoicing B2B
    /// en recette. GÂTÉ PAR L'ENVIRONNEMENT PA (BUG-23) : la dérogation n'est appliquée QUE lorsque l'appelant
    /// passe <c>allowSandboxTestSirens = true</c> (contexte PA Staging/Sandbox) ; en PRODUCTION la clé de Luhn
    /// reste stricte — jamais d'affaiblissement silencieux d'une validation Blocking (CLAUDE.md n°3).
    /// </summary>
    private static readonly HashSet<string> PaSandboxTestSirens = new(StringComparer.Ordinal)
    {
        "000000001",
        "000000002",
    };

    /// <summary>Indique si <paramref name="siren"/> est un SIREN valide (9 chiffres + Luhn ; ou La Poste ; ou SIREN de test sandbox PA UNIQUEMENT si <paramref name="allowSandboxTestSirens"/>).</summary>
    /// <param name="siren">Le SIREN à contrôler (absent = <c>null</c>).</param>
    /// <param name="allowSandboxTestSirens">
    /// Autorise les SIREN de test sandbox PA (<see cref="PaSandboxTestSirens"/>) — à passer <c>true</c> UNIQUEMENT
    /// quand le compte PA actif n'est PAS en production (BUG-23). Défaut <c>false</c> = STRICT (clé de Luhn exigée,
    /// production-safe) : aucun appelant ne tolère ces faux SIREN par accident (CLAUDE.md n°3).
    /// </param>
    /// <returns><c>true</c> si le SIREN est valide, sinon <c>false</c>.</returns>
    public static bool IsValid(string? siren, bool allowSandboxTestSirens = false)
    {
        if (string.IsNullOrEmpty(siren) || siren.Length != 9)
        {
            return false;
        }

        if (siren == LaPosteSiren || (allowSandboxTestSirens && PaSandboxTestSirens.Contains(siren)))
        {
            return true;
        }

        return Luhn.IsValid(siren);
    }

    /// <summary>
    /// Indique si <paramref name="siren"/> est BIEN FORMÉ (9 chiffres), SANS contrôle de la clé de Luhn.
    /// Réservé au SIREN ÉMETTEUR PARAMÉTRÉ (donnée de confiance du tenant) : autorise les SIREN de TEST des
    /// sandboxes PA (décision de recette Karl, 18/06/2026). NE PAS utiliser pour un SIREN EXTRAIT (acheteur),
    /// qui reste contrôlé par <see cref="IsValid"/> (clé de Luhn, F04 §4.1).
    /// </summary>
    public static bool IsWellFormed(string? siren)
    {
        if (string.IsNullOrEmpty(siren) || siren.Length != 9)
        {
            return false;
        }

        foreach (var c in siren)
        {
            if (c < '0' || c > '9')
            {
                return false;
            }
        }

        return true;
    }
}
