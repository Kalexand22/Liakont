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

    /// <summary>Colonne <c>code_regime</c> de la ligne (code régime TVA brut, R3).</summary>
    internal const string ColCodeRegime = "code_regime";

    /// <summary>Colonne <c>no_ligne</c> (référence de la ligne dans la source).</summary>
    internal const string ColNoLigne = "no_ligne";

    /// <summary>
    /// Colonne <c>montant_ligne</c> (lignes type 3 — montant ENCAISSÉ, nommée explicitement par F09 §5.1 :
    /// « <c>lignes_ba.type_ligne='3'</c> → <c>montant_ligne</c>, <c>date_reglement</c>… »). DISTINCTE de
    /// <see cref="ColMontantHt"/> (montant des lignes de DOCUMENT type 4/2, F01-F02 §4.3) : lire <c>montant_ht</c>
    /// pour un règlement sous-déclarerait l'encaissement si le schéma réel place le montant dans
    /// <c>montant_ligne</c>. RÉSERVE : validé sur fixtures uniquement, réconcilié au schéma Pervasive réel par
    /// GATE_DEMO_ISATECH (cf. en-tête de cette classe).
    /// </summary>
    internal const string ColMontantLigne = "montant_ligne";

    /// <summary>Colonne <c>date_reglement</c> (lignes type 3 — date d'encaissement, F09 ; axe de période des paiements).</summary>
    internal const string ColDateReglement = "date_reglement";

    /// <summary>Colonne <c>mode_reglement</c> (lignes type 3 — CB, chèque, espèces, virement ; informatif F09).</summary>
    internal const string ColModeReglement = "mode_reglement";

    /// <summary>Colonne <c>no_remise</c> (lignes type 3 — référence source de l'encaissement, F09).</summary>
    internal const string ColNoRemise = "no_remise";

    /// <summary>Alias <c>origin_no_ba</c> : <c>no_ba</c> du bordereau d'origine d'un avoir (auto-jointure via <c>no_ba_lettrage</c>, ADP03).</summary>
    internal const string ColOriginNoBa = "origin_no_ba";

    /// <summary>Alias <c>origin_numero_piece</c> : numéro de pièce de la facture d'origine d'un avoir (lien EN 16931 BT-25, ADP03).</summary>
    internal const string ColOriginNumeroPiece = "origin_numero_piece";

    /// <summary>Alias <c>origin_date_vente</c> : date d'émission de la facture d'origine d'un avoir (ADP03).</summary>
    internal const string ColOriginDateVente = "origin_date_vente";

    /// <summary>Type de pièce source « bordereau de vente » (filtre des documents extraits).</summary>
    internal const string PieceVente = "B";

    /// <summary>Type de pièce source « avoir » (« copie en positif » lettrée à son origine — F07-F08 §B.2, ADP03).</summary>
    internal const string PieceAvoir = "A";

    /// <summary>
    /// Requête d'extraction des DOCUMENTS (ventes ET avoirs) d'une période — F01-F02 §4.3, F07-F08 §B.5 :
    /// <c>entete_ba WHERE bordereau_ou_avoir IN ('B','A') AND date_vente ∈ [from, to[</c>, jointe en LEFT JOIN à ses
    /// lignes de document (<c>type_ligne IN ('4','2')</c> dans la clause ON), triée par <c>no_ba</c> puis <c>no_ligne</c>
    /// pour permettre un regroupement par bordereau EN STREAMING (R8 — un seul lecteur, mémoire O(1 doc)).
    /// <para>
    /// Le LEFT JOIN (filtre de type dans le ON) garantit qu'une vente sans ligne 4/2 N'EST PAS omise silencieusement : elle ressort en ligne d'entête seule (colonnes ligne NULL) et le document est émis sans ligne (jamais de perte d'une vente — « bloquer plutôt qu'envoyer faux »). Les règlements (<c>type_ligne='3'</c>) restent EXCLUS ici : ils sont extraits par <see cref="SelectPaymentsSql"/> (F09). Le code régime brut est porté par <c>lignes_ba.code_regime</c> (R3 — aucune
    /// jointure <c>Regime_tva</c> nécessaire pour le document ; le libellé du régime est exposé par
    /// <c>ListSourceTaxRegimes</c>, ADP04). Période sur <c>date_vente</c> conformément à la requête
    /// documentée : un document antidaté saisi tardivement reste extractible via la fenêtre de
    /// recouvrement de l'ordonnanceur agent + l'idempotence anti-re-push (contrat IExtractor « DISPONIBLE
    /// DEPUIS ») — EncheresV6 n'expose aucun horodatage d'insertion monotone (ne pas inventer de colonne).
    /// </para>
    /// <para>
    /// AVOIRS (ADP03, F07-F08 §B.2/§B.5) : un avoir (<c>'A'</c>) référence sa facture d'origine via
    /// <c>no_ba_lettrage</c>. La SECONDE auto-jointure LEFT JOIN (<c>o.no_ba = e.no_ba_lettrage</c>, restreinte aux
    /// avoirs par <c>e.bordereau_ou_avoir='A'</c> dans le ON) rapporte l'entête d'origine SANS requête supplémentaire
    /// ni second lecteur (lecture seule, O(1 doc) préservé) : un même lecteur, jamais de MARS. Les colonnes
    /// <c>origin_no_ba/origin_numero_piece/origin_date_vente</c> sont NULL pour une vente et pour un avoir dont le
    /// lettrage ne résout pas (avoir alors BLOQUÉ par le mapper, jamais deviné — ADR-0004 D3-3, F07-F08 §B.4).
    /// Le sens « crédit » est porté par le TYPE de pièce, jamais par le signe : les montants restent positifs (F07-F08 §B.2).
    /// </para>
    /// Bornes positionnelles ODBC (<c>?</c>) : <c>date_vente &gt;= from</c> (incluse), <c>&lt; to</c> (exclue).
    /// </summary>
    internal const string SelectDocumentsSql =
        "SELECT e." + ColNoBa + ", e." + ColNumeroPiece + ", e." + ColBordereauOuAvoir + ", e." + ColDateVente
        + ", e." + ColNoBaLettrage + ", e." + ColAcheteurNom + ", e." + ColSociete + ", e." + ColAcheteurSiren
        + ", e." + ColAcheteurVille + ", e." + ColAcheteurCodePostal + ", e." + ColAcheteurPays
        + ", e." + ColTotalHt + ", e." + ColTotalTva + ", e." + ColTotalBordereau
        + ", o." + ColNoBa + " AS " + ColOriginNoBa + ", o." + ColNumeroPiece + " AS " + ColOriginNumeroPiece
        + ", o." + ColDateVente + " AS " + ColOriginDateVente
        + ", l." + ColTypeLigne + ", l." + ColDesignation + ", l." + ColMontantHt + ", l." + ColMontantTva
        + ", l." + ColTauxTva + ", l." + ColQuantite + ", l." + ColPrixUnitaire + ", l." + ColCodeRegime
        + ", l." + ColNoLigne
        + " FROM " + TableEntete + " e"
        + " LEFT JOIN " + TableLignes + " l ON l." + ColNoBa + " = e." + ColNoBa
        + " AND l." + ColTypeLigne + " IN ('" + EncheresV6RowMapper.LigneAdjudication + "', '" + EncheresV6RowMapper.LigneFrais + "')"
        + " LEFT JOIN " + TableEntete + " o ON e." + ColBordereauOuAvoir + " = '" + PieceAvoir + "'"
        + " AND o." + ColNoBa + " = e." + ColNoBaLettrage
        + " WHERE e." + ColBordereauOuAvoir + " IN ('" + PieceVente + "', '" + PieceAvoir + "')"
        + " AND e." + ColDateVente + " >= ? AND e." + ColDateVente + " < ?"
        + " ORDER BY e." + ColNoBa + ", l." + ColNoLigne;

    /// <summary>
    /// Requête d'extraction des ENCAISSEMENTS d'une période — F09 (e-reporting de paiement), ADP03 :
    /// <c>lignes_ba WHERE type_ligne='3' AND date_reglement ∈ [from, to[</c>, jointe (INNER JOIN) à son
    /// bordereau (<c>entete_ba</c>) pour rapporter le numéro de pièce d'origine (rattachement par lettrage), triée
    /// par <c>no_ba</c> puis <c>no_ligne</c> (ordre stable).
    /// <para>
    /// Le montant encaissé est lu dans <see cref="ColMontantLigne"/> (<c>montant_ligne</c>) — colonne nommée
    /// explicitement par F09 §5.1 pour les lignes type 3, distincte du <c>montant_ht</c> des lignes de document.
    /// Période sur <c>date_reglement</c> (la date d'encaissement, pas la date de vente). L'INNER JOIN exige qu'un
    /// règlement soit rattaché à un bordereau : un encaissement orphelin n'est pas transmis (jamais inventé).
    /// L'AGRÉGATION jour × taux est faite par la PLATEFORME (PIP03) ; l'adaptateur transmet les paiements BRUTS (F09).
    /// </para>
    /// Bornes positionnelles ODBC (<c>?</c>) : <c>date_reglement &gt;= from</c> (incluse), <c>&lt; to</c> (exclue).
    /// </summary>
    internal const string SelectPaymentsSql =
        "SELECT e." + ColNoBa + ", e." + ColNumeroPiece
        + ", l." + ColNoLigne + ", l." + ColMontantLigne + ", l." + ColDateReglement
        + ", l." + ColModeReglement + ", l." + ColNoRemise
        + " FROM " + TableLignes + " l"
        + " INNER JOIN " + TableEntete + " e ON e." + ColNoBa + " = l." + ColNoBa
        + " WHERE l." + ColTypeLigne + " = '" + EncheresV6RowMapper.LigneReglement + "'"
        + " AND l." + ColDateReglement + " >= ? AND l." + ColDateReglement + " < ?"
        + " ORDER BY e." + ColNoBa + ", l." + ColNoLigne;

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
