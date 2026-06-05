namespace Liakont.Agent.Adapters.EncheresV6.Source;

using System;

/// <summary>
/// Connaissance du SCHÉMA EncheresV6 (tables / colonnes) et requêtes SQL en LECTURE SEULE du
/// <see cref="PervasiveExtractor"/> (F01-F02 §4.3, ADP02). Centralisé en un seul endroit : c'est ici
/// — et nulle part ailleurs — que vivent les noms de tables/colonnes et le texte des requêtes.
/// <para>
/// RÉSERVE ASSUMÉE ET TRACÉE (GATE_ADAPTER_ENCHERESV6 / GATE_DEMO_ISATECH) : la base Pervasive réelle
/// n'est jamais sur la machine d'orchestration. Les noms explicitement donnés par la spec sont
/// <c>entete_ba.societe</c> et <c>entete_ba.total_bordereau</c> (F01-F02 §4.3) ; les autres colonnes
/// suivent le modèle de fixtures (qui modélise ce que l'ODBC renverra). « Validé sur fixtures » ≠
/// « validé sur le schéma réel » : un écart de nommage découvert au test ODBC réel (serveur dédié,
/// GATE_DEMO_ISATECH) se corrige ICI, en un seul point.
/// </para>
/// <para>
/// Ce n'est PAS une donnée client (CLAUDE.md n°7) : c'est la connaissance du schéma EncheresV6 (un
/// PRODUIT), pas un SIREN ni une chaîne ODBC (paramétrage de tenant, lot CMP). Aucun montant n'est
/// recalculé ici (R3) ; le mapping vers le pivot reste <see cref="EncheresV6RowMapper"/>.
/// </para>
/// </summary>
internal static class EncheresV6Schema
{
    /// <summary>Table d'entête des bordereaux (ventes et avoirs) — <c>entete_ba</c>.</summary>
    internal const string TableEntete = "entete_ba";

    /// <summary>Table des lignes de bordereau (adjudication, frais, règlements) — <c>lignes_ba</c>.</summary>
    internal const string TableLignes = "lignes_ba";

    /// <summary>Table des régimes de TVA source — <c>Regime_tva</c>.</summary>
    internal const string TableRegimes = "Regime_tva";

    /// <summary>Colonne <c>no_ba</c> (identifiant interne du bordereau, clé de jointure et référence source).</summary>
    internal const string ColNoBa = "no_ba";

    /// <summary>Colonne <c>numero_piece</c> (numéro de pièce affiché, EN 16931 BT-1).</summary>
    internal const string ColNumeroPiece = "numero_piece";

    /// <summary>Colonne <c>bordereau_ou_avoir</c> (« B » vente, « A » avoir — transporté brut).</summary>
    internal const string ColBordereauOuAvoir = "bordereau_ou_avoir";

    /// <summary>Colonne <c>date_vente</c> (date d'émission, EN 16931 BT-2 ; axe de période).</summary>
    internal const string ColDateVente = "date_vente";

    /// <summary>Colonne <c>no_ba_lettrage</c> (pour un avoir : no_ba du bordereau d'origine).</summary>
    internal const string ColNoBaLettrage = "no_ba_lettrage";

    /// <summary>Colonne <c>acheteur_nom</c> (nom du destinataire).</summary>
    internal const string ColAcheteurNom = "acheteur_nom";

    /// <summary>Colonne <c>societe</c> de l'entête (non vide ⇒ <c>IsCompanyHint</c> brut — spec §4.3).</summary>
    internal const string ColSociete = "societe";

    /// <summary>Colonne <c>acheteur_siren</c> (SIREN acheteur si présent — rare en B2C).</summary>
    internal const string ColAcheteurSiren = "acheteur_siren";

    /// <summary>Colonne <c>acheteur_ville</c>.</summary>
    internal const string ColAcheteurVille = "acheteur_ville";

    /// <summary>Colonne <c>acheteur_code_postal</c>.</summary>
    internal const string ColAcheteurCodePostal = "acheteur_code_postal";

    /// <summary>Colonne <c>acheteur_pays</c> (ISO 3166-1 alpha-2).</summary>
    internal const string ColAcheteurPays = "acheteur_pays";

    /// <summary>Colonne <c>total_ht</c> d'entête.</summary>
    internal const string ColTotalHt = "total_ht";

    /// <summary>Colonne <c>total_tva</c> d'entête.</summary>
    internal const string ColTotalTva = "total_tva";

    /// <summary>Colonne <c>total_bordereau</c> d'entête (total TTC stocké — spec §4.3).</summary>
    internal const string ColTotalBordereau = "total_bordereau";

    /// <summary>Colonne <c>type_ligne</c> (« 4 » adjudication, « 2 » frais, « 3 » règlement).</summary>
    internal const string ColTypeLigne = "type_ligne";

    /// <summary>Colonne <c>designation</c> (libellé de la ligne).</summary>
    internal const string ColDesignation = "designation";

    /// <summary>Colonne <c>montant_ht</c> de la ligne (flottant source).</summary>
    internal const string ColMontantHt = "montant_ht";

    /// <summary>Colonne <c>montant_tva</c> de la ligne (flottant source).</summary>
    internal const string ColMontantTva = "montant_tva";

    /// <summary>Colonne <c>taux_tva</c> de la ligne (%, nullable).</summary>
    internal const string ColTauxTva = "taux_tva";

    /// <summary>Colonne <c>quantite</c> de la ligne (nullable).</summary>
    internal const string ColQuantite = "quantite";

    /// <summary>Colonne <c>prix_unitaire</c> de la ligne (flottant source, nullable).</summary>
    internal const string ColPrixUnitaire = "prix_unitaire";

    /// <summary>Colonne <c>code_regime</c> (code régime TVA brut, R3) — portée par <c>lignes_ba</c> ET clé de <c>Regime_tva</c>.</summary>
    internal const string ColCodeRegime = "code_regime";

    /// <summary>Colonne <c>libelle</c> de la table <c>Regime_tva</c> (libellé du régime tel que stocké en source).</summary>
    internal const string ColLibelleRegime = "libelle";

    /// <summary>Alias de l'agrégat <c>COUNT</c> des occurrences d'un régime dans <c>lignes_ba</c> (requête <see cref="SelectTaxRegimesSql"/>).</summary>
    internal const string ColRegimeOccurrences = "occurrences";

    /// <summary>Colonne <c>no_ligne</c> (référence de la ligne dans la source).</summary>
    internal const string ColNoLigne = "no_ligne";

    /// <summary>Type de pièce source « bordereau de vente » (filtre des documents extraits en ADP02).</summary>
    internal const string PieceVente = "B";

    /// <summary>
    /// Requête d'extraction des DOCUMENTS (ventes) d'une période — F01-F02 §4.3 :
    /// <c>entete_ba WHERE bordereau_ou_avoir='B' AND date_vente ∈ [from, to[</c>, jointe en LEFT JOIN à ses lignes
    /// de document (<c>type_ligne IN ('4','2')</c> dans la clause ON), triée par <c>no_ba</c> puis <c>no_ligne</c> pour
    /// permettre un regroupement par bordereau EN STREAMING (R8 — un seul lecteur, mémoire O(1 doc)).
    /// <para>
    /// Le LEFT JOIN (filtre de type dans le ON) garantit qu'une vente sans ligne 4/2 N'EST PAS omise silencieusement : elle ressort en ligne d'entête seule (colonnes ligne NULL) et le document est émis sans ligne (jamais de perte d'une vente — « bloquer plutôt qu'envoyer faux »). Les avoirs (<c>'A'</c>) et les règlements (<c>type_ligne='3'</c>) sont volontairement EXCLUS ici :
    /// ils relèvent d'ADP03. Le code régime brut est porté par <c>lignes_ba.code_regime</c> (R3 — aucune
    /// jointure <c>Regime_tva</c> nécessaire pour le document ; le libellé du régime est exposé par
    /// <c>ListSourceTaxRegimes</c>, ADP04). Période sur <c>date_vente</c> conformément à la requête
    /// documentée : un document antidaté saisi tardivement reste extractible via la fenêtre de
    /// recouvrement de l'ordonnanceur agent + l'idempotence anti-re-push (contrat IExtractor « DISPONIBLE
    /// DEPUIS ») — EncheresV6 n'expose aucun horodatage d'insertion monotone (ne pas inventer de colonne).
    /// </para>
    /// Bornes positionnelles ODBC (<c>?</c>) : <c>date_vente &gt;= from</c> (incluse), <c>&lt; to</c> (exclue).
    /// </summary>
    internal const string SelectDocumentsSql =
        "SELECT e." + ColNoBa + ", e." + ColNumeroPiece + ", e." + ColBordereauOuAvoir + ", e." + ColDateVente
        + ", e." + ColNoBaLettrage + ", e." + ColAcheteurNom + ", e." + ColSociete + ", e." + ColAcheteurSiren
        + ", e." + ColAcheteurVille + ", e." + ColAcheteurCodePostal + ", e." + ColAcheteurPays
        + ", e." + ColTotalHt + ", e." + ColTotalTva + ", e." + ColTotalBordereau
        + ", l." + ColTypeLigne + ", l." + ColDesignation + ", l." + ColMontantHt + ", l." + ColMontantTva
        + ", l." + ColTauxTva + ", l." + ColQuantite + ", l." + ColPrixUnitaire + ", l." + ColCodeRegime
        + ", l." + ColNoLigne
        + " FROM " + TableEntete + " e"
        + " LEFT JOIN " + TableLignes + " l ON l." + ColNoBa + " = e." + ColNoBa
        + " AND l." + ColTypeLigne + " IN ('" + EncheresV6RowMapper.LigneAdjudication + "', '" + EncheresV6RowMapper.LigneFrais + "')"
        + " WHERE e." + ColBordereauOuAvoir + " = '" + PieceVente + "'"
        + " AND e." + ColDateVente + " >= ? AND e." + ColDateVente + " < ?"
        + " ORDER BY e." + ColNoBa + ", l." + ColNoLigne;

    /// <summary>
    /// Requête de listage des RÉGIMES de TVA source (ADP04, F03/TVA03) : tous les régimes déclarés dans
    /// <c>Regime_tva</c> (code BRUT + libellé), avec le nombre d'occurrences de chaque code dans
    /// <c>lignes_ba</c>. Le <c>LEFT JOIN</c> garantit qu'un régime déclaré mais jamais utilisé ressort
    /// avec <c>occurrences = 0</c> (jamais omis) ; <c>COUNT(l.code_regime)</c> (et non <c>COUNT(*)</c>)
    /// compte 0 pour la ligne d'entête seule produite par le <c>LEFT JOIN</c> sans correspondance.
    /// <para>
    /// LECTURE SEULE STRICTE (<c>SELECT</c> + <c>GROUP BY</c>, aucune écriture). L'adaptateur n'interprète
    /// jamais le régime (R3, CLAUDE.md n°2) : il transporte le code et le libellé bruts ; le mapping F03
    /// et la détection de couverture (TVA03) sont plateforme. Tri par <c>code_regime</c> pour un résultat
    /// DÉTERMINISTE. Sémantique identique au mode fixtures (<see cref="EncheresV6FixtureExtractor.ListSourceTaxRegimes"/>) :
    /// liste pilotée par <c>Regime_tva</c>, occurrences comptées sur <c>lignes_ba</c>.
    /// </para>
    /// </summary>
    internal const string SelectTaxRegimesSql =
        "SELECT r." + ColCodeRegime + ", r." + ColLibelleRegime + ", COUNT(l." + ColCodeRegime + ") AS " + ColRegimeOccurrences
        + " FROM " + TableRegimes + " r"
        + " LEFT JOIN " + TableLignes + " l ON l." + ColCodeRegime + " = r." + ColCodeRegime
        + " GROUP BY r." + ColCodeRegime + ", r." + ColLibelleRegime
        + " ORDER BY r." + ColCodeRegime;

    /// <summary>Tables dont la présence est contrôlée par <c>CheckHealth</c> (accès + comptage rapide).</summary>
    internal static readonly string[] ExpectedTables = { TableEntete, TableLignes, TableRegimes };

    /// <summary>Requête de comptage rapide (présence + accessibilité) d'une table — LECTURE SEULE.</summary>
    /// <param name="table">Nom de la table (issu de <see cref="ExpectedTables"/>, jamais une entrée utilisateur).</param>
    /// <returns>Le <c>SELECT COUNT(*)</c> correspondant.</returns>
    internal static string CountSql(string table) => "SELECT COUNT(*) FROM " + table;

    /// <summary>
    /// Garde de LECTURE SEULE STRICTE (CLAUDE.md n°5, F01-F02 R1) appliquée à TOUTE requête avant
    /// exécution : rejette toute commande qui n'est pas un <c>SELECT</c>. Défense en profondeur — toutes
    /// les requêtes de cet adaptateur sont des constantes <c>SELECT</c>, la garde empêche qu'une
    /// modification future n'introduise une écriture sans être détectée (le test-espion la double).
    /// </summary>
    /// <param name="sql">La requête à exécuter.</param>
    /// <exception cref="InvalidOperationException">Si <paramref name="sql"/> n'est pas un SELECT.</exception>
    internal static void EnsureSelectOnly(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)
            || !sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Requête non autorisée sur la base source EncheresV6 : l'adaptateur est en lecture seule "
                + "stricte (aucune écriture, aucun verrou — CLAUDE.md n°5). Seules les requêtes SELECT sont permises.");
        }
    }
}
