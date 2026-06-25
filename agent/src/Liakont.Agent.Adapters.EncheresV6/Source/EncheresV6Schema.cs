namespace Liakont.Agent.Adapters.EncheresV6.Source;

using System;
using System.Text.RegularExpressions;

/// <summary>
/// Connaissance du SCHÉMA EncheresV6 (tables / colonnes) et requêtes SQL en LECTURE SEULE de
/// l'extracteur ODBC (F01-F02 §4.3). Centralisé en un seul endroit : c'est ici — et nulle part
/// ailleurs — que vivent les noms de tables/colonnes et le texte des requêtes.
/// <para>
/// INSTANCE (et non statique) car le PRÉFIXE DE SCHÉMA est paramétrable : la base réelle Pervasive
/// expose des tables NUES (<c>entete_ba</c>), la base de démo SQL Server les expose sous un schéma
/// (<c>[enc].[entete_ba]</c>). Le préfixe est du PARAMÉTRAGE de tenant (jamais une entrée libre :
/// validé <c>^[A-Za-z_][A-Za-z0-9_]*$</c>), pas une donnée client (CLAUDE.md n°7). Aucun montant n'est
/// recalculé ici (R3) ; le mapping vers le pivot reste <see cref="EncheresV6RowMapper"/>.
/// </para>
/// <para>
/// MODÈLE (vérifié sur la donnée + dictionnaire Magic XPA) : le document ACHETEUR (BA) porte la
/// commission acheteur (lignes <c>type_ligne='1'</c>, <c>montant_frais_ht</c>) ; le document VENDEUR
/// (BV) porte la commission vendeur (lignes <c>type_ligne='2'</c>, <c>mtt_frais_ht</c>). Le code régime
/// vit sur <c>ligne_pv</c>, joint depuis la ligne acheteur par l'identifiant GLOBAL de ligne de PV
/// (<c>lignes_ba.no_ligne_tout_pv</c> = <c>ligne_pv.no_ligne_tout_pv</c>) : <c>ligne_pv.no_ba</c> vaut
/// très souvent 0 (le lien acheteur n'est pas porté sur la ligne de PV), si bien que l'ancienne jointure
/// par <c>no_ba</c>+<c>no_ligne_pv</c> ratait et laissait l'adjudication sans code régime. Filtre tenant
/// par <c>No_dossier</c> (BA <c>No_dossier_comptable</c>, BV <c>dossier_comptable</c>).
/// </para>
/// </summary>
internal sealed class EncheresV6Schema
{
    // ── Tables ──────────────────────────────────────────────────────
    internal const string TableEnteteBa = "entete_ba";
    internal const string TableLignesBa = "lignes_ba";
    internal const string TableEnteteBv = "entete_bv";
    internal const string TableLignesBv = "lignes_bv";
    internal const string TableLignePv = "ligne_pv";
    internal const string TableRegimes = "Regime_tva";

    // ── Colonnes communes / BA ──────────────────────────────────────
    internal const string ColNoBa = "no_ba";
    internal const string ColBordereauOuAvoir = "bordereau_ou_avoir";
    internal const string ColDateVente = "date_vente";
    internal const string ColNoBaLettrage = "no_ba_lettrage";
    internal const string ColNoDossierBa = "No_dossier_comptable";
    internal const string ColNom = "nom";
    internal const string ColPrenom = "prenom";
    internal const string ColSociete = "societe";
    internal const string ColAcheteurSiren = "acheteur_siren";
    internal const string ColTvaCee = "tva_cee";
    internal const string ColAdresse = "adresse";
    internal const string ColCodePostal = "code_postal";
    internal const string ColVille = "ville";
    internal const string ColCodePays = "code_pays";
    internal const string ColCodeExport = "code_export";
    internal const string ColModeLivraison = "mode_livraison";
    internal const string ColTotalBordereau = "total_bordereau";

    // ── Lignes BA ───────────────────────────────────────────────────
    internal const string ColTypeLigne = "type_ligne";
    internal const string ColCodeLigne = "code_ligne";
    internal const string ColNoLignePv = "no_ligne_pv";
    internal const string ColNoLigneToutPv = "no_ligne_tout_pv";
    internal const string ColLibelleLigne = "libelle_ligne";
    internal const string ColMontantAdjHt = "montant_adj_ht";
    internal const string ColMttTvaInclusAdj = "mtt_tva_inclus_adj";
    internal const string ColMttTvaEnPlusAdj = "mtt_tva_en_plus_adj";
    internal const string ColMontantFraisHt = "montant_frais_ht";
    internal const string ColMontantTvaFrais = "montant_tva_frais";
    internal const string ColMontantLigne = "montant_ligne";
    internal const string ColCodeDevise = "code_devise";
    internal const string ColDateReglement = "date_reglement";
    internal const string ColNoRemise = "no_remise";

    // ── BV ──────────────────────────────────────────────────────────
    internal const string ColNoBv = "no_bv";
    internal const string ColNoBvLettrage = "no_bv_lettrage";
    internal const string ColNoDossierBv = "dossier_comptable";
    internal const string ColCodeRegimeTva = "code_regime_tva";
    internal const string ColMttFraisHt = "mtt_frais_ht";
    internal const string ColMttTvaFrais = "mtt_tva_frais";

    // ── ligne_pv ────────────────────────────────────────────────────
    internal const string ColPvNoLigneToutPv = "no_ligne_tout_pv";

    // ── Régimes ─────────────────────────────────────────────────────
    internal const string ColRegimeLibelle = "libelle_descriptif";
    internal const string ColRegimeOccurrences = "occurrences";

    /// <summary>Alias du libellé de régime dans <see cref="SelectTaxRegimesSql"/> (consommé par l'extracteur).</summary>
    internal const string ColLibelleAlias = "libelle";

    // ── Alias de jointure ──────────────────────────────────────────
    internal const string ColCodeRegime = "code_regime";
    internal const string ColOriginNoBa = "origin_no_ba";
    internal const string ColOriginDateVente = "origin_date_vente";
    internal const string ColOriginNoBv = "origin_no_bv";

    // ── Types de pièce ──────────────────────────────────────────────
    internal const string PieceVente = "B";
    internal const string PieceAvoir = "A";

    // ── Types de ligne (ASYMÉTRIE acheteur/vendeur, sourcé dico Magic + donnée) ──
    internal const string LigneLotBa = "1";        // lignes_ba : lot (adjudication + commission acheteur)
    internal const string LigneDebooursBa = "2";   // lignes_ba : débours/annexes acheteur (hors marge)
    internal const string LigneReglementBa = "3";  // lignes_ba : règlement
    internal const string LigneRecapBa = "4";      // lignes_ba : RÉCAP (ignoré)
    internal const string LigneLotBv = "1";        // lignes_bv : lot (adjudication)
    internal const string LigneCommissionBv = "2"; // lignes_bv : commission vendeur (marge)
    internal const string LigneDeboursBv = "3";    // lignes_bv : débours (hors marge)
    internal const string LignePaiementBv = "4";   // lignes_bv : paiement

    private static readonly Regex SchemaPattern = new Regex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    private readonly string _enteteBa;
    private readonly string _lignesBa;
    private readonly string _enteteBv;
    private readonly string _lignesBv;
    private readonly string _lignePv;
    private readonly string _regimes;

    /// <summary>Crée la connaissance du schéma pour un préfixe donné (vide = tables nues Pervasive ; ex. « enc » pour la démo SQL Server).</summary>
    /// <param name="schema">Préfixe de schéma (paramétrage). <c>null</c>/vide = aucun préfixe. Validé contre l'injection.</param>
    /// <exception cref="ArgumentException">Si <paramref name="schema"/> n'est pas un identifiant SQL simple.</exception>
    public EncheresV6Schema(string? schema)
    {
        string prefix;
        if (string.IsNullOrWhiteSpace(schema))
        {
            prefix = string.Empty;
        }
        else if (!SchemaPattern.IsMatch(schema!.Trim()))
        {
            throw new ArgumentException(
                "Le schéma EncheresV6 (adapterConfig.EncheresV6.schema) doit être un identifiant SQL simple "
                + "(lettres, chiffres, « _ », sans espace ni point).",
                nameof(schema));
        }
        else
        {
            prefix = schema!.Trim() + ".";
        }

        _enteteBa = prefix + TableEnteteBa;
        _lignesBa = prefix + TableLignesBa;
        _enteteBv = prefix + TableEnteteBv;
        _lignesBv = prefix + TableLignesBv;
        _lignePv = prefix + TableLignePv;
        _regimes = prefix + TableRegimes;
    }

    /// <summary>Tables dont la présence est contrôlée par <c>CheckHealth</c>.</summary>
    public string[] ExpectedTables => new[] { _enteteBa, _lignesBa, _enteteBv, _lignesBv, _lignePv, _regimes };

    /// <summary>
    /// Requête des DOCUMENTS ACHETEUR (BA : ventes + avoirs) d'une période, lignes de LOT (type 1) jointes
    /// + régime du lot (<c>ligne_pv</c>) + entête d'origine d'un avoir (auto-jointure). Bornes
    /// positionnelles ODBC : <c>dossier</c>, <c>from</c> (incluse), <c>to</c> (exclue). Streaming par <c>no_ba</c>.
    /// </summary>
    public string SelectBaDocumentsSql =>
        "SELECT e." + ColNoBa + ", e." + ColBordereauOuAvoir + ", e." + ColDateVente + ", e." + ColNoBaLettrage
        + ", e." + ColNom + ", e." + ColPrenom + ", e." + ColSociete + ", e." + ColAcheteurSiren + ", e." + ColTvaCee
        + ", e." + ColAdresse + ", e." + ColCodePostal + ", e." + ColVille + ", e." + ColCodePays
        + ", e." + ColCodeExport + ", e." + ColModeLivraison
        + ", e." + ColTotalBordereau
        + ", o." + ColNoBa + " AS " + ColOriginNoBa + ", o." + ColDateVente + " AS " + ColOriginDateVente
        + ", l." + ColTypeLigne + ", l." + ColCodeLigne + ", l." + ColNoLignePv + ", l." + ColNoLigneToutPv + ", l." + ColLibelleLigne
        + ", l." + ColMontantAdjHt + ", l." + ColMttTvaInclusAdj + ", l." + ColMttTvaEnPlusAdj
        + ", l." + ColMontantFraisHt + ", l." + ColMontantTvaFrais + ", l." + ColCodeDevise
        + ", lp." + ColCodeRegimeTva + " AS " + ColCodeRegime
        + " FROM " + _enteteBa + " e"
        + " LEFT JOIN " + _lignesBa + " l ON l." + ColNoBa + " = e." + ColNoBa + " AND l." + ColTypeLigne + " = '" + LigneLotBa + "'"
        + " LEFT JOIN " + _lignePv + " lp ON lp." + ColPvNoLigneToutPv + " = l." + ColNoLigneToutPv
        + " LEFT JOIN " + _enteteBa + " o ON e." + ColBordereauOuAvoir + " = '" + PieceAvoir + "' AND o." + ColNoBa + " = e." + ColNoBaLettrage
        + " WHERE e." + ColNoDossierBa + " = ? AND e." + ColBordereauOuAvoir + " IN ('" + PieceVente + "', '" + PieceAvoir + "')"
        + " AND e." + ColDateVente + " >= ? AND e." + ColDateVente + " < ?"
        + " ORDER BY e." + ColNoBa + ", l." + ColNoLignePv;

    /// <summary>
    /// Requête des DOCUMENTS VENDEUR (BV : ventes + avoirs) d'une période, lignes de LOT (type 1) + commission
    /// (type 2) jointes — LECTURE DIRECTE par <c>no_bv</c> (PAS de jointure <c>ligne_pv</c> : éviter le
    /// produit cartésien qui doublerait la commission). Débours (type 3) et paiements (type 4) EXCLUS.
    /// Bornes ODBC : <c>dossier</c>, <c>from</c>, <c>to</c>. Streaming par <c>no_bv</c>.
    /// </summary>
    public string SelectBvDocumentsSql =>
        "SELECT e." + ColNoBv + ", e." + ColBordereauOuAvoir + ", e." + ColDateVente + ", e." + ColNoBvLettrage
        + ", e." + ColNom + ", e." + ColPrenom + ", e." + ColCodePostal + ", e." + ColVille + ", e." + ColCodePays
        + ", e." + ColCodeRegimeTva + ", e." + ColTotalBordereau
        + ", o." + ColNoBv + " AS " + ColOriginNoBv + ", o." + ColDateVente + " AS " + ColOriginDateVente
        + ", l." + ColTypeLigne + ", l." + ColCodeLigne + ", l." + ColNoLignePv + ", l." + ColLibelleLigne
        + ", l." + ColMontantAdjHt + ", l." + ColMttFraisHt + ", l." + ColMttTvaFrais + ", l." + ColCodeDevise
        + " FROM " + _enteteBv + " e"
        + " LEFT JOIN " + _lignesBv + " l ON l." + ColNoBv + " = e." + ColNoBv
        + " AND l." + ColTypeLigne + " IN ('" + LigneLotBv + "', '" + LigneCommissionBv + "')"
        + " LEFT JOIN " + _enteteBv + " o ON e." + ColBordereauOuAvoir + " = '" + PieceAvoir + "' AND o." + ColNoBv + " = e." + ColNoBvLettrage
        + " WHERE e." + ColNoDossierBv + " = ? AND e." + ColBordereauOuAvoir + " IN ('" + PieceVente + "', '" + PieceAvoir + "')"
        + " AND e." + ColDateVente + " >= ? AND e." + ColDateVente + " < ?"
        + " ORDER BY e." + ColNoBv + ", l." + ColTypeLigne + ", l." + ColNoLignePv;

    /// <summary>
    /// Requête des ENCAISSEMENTS (lignes BA type 3) d'une période — F09. Bornes ODBC : <c>dossier</c>,
    /// <c>from</c>, <c>to</c> (sur <c>date_reglement</c>). Montant dans <c>montant_ligne</c>.
    /// </summary>
    public string SelectPaymentsSql =>
        "SELECT e." + ColNoBa + ", l." + ColNoLignePv + ", l." + ColCodeLigne + ", l." + ColLibelleLigne
        + ", l." + ColMontantLigne + ", l." + ColDateReglement + ", l." + ColNoRemise
        + " FROM " + _lignesBa + " l"
        + " INNER JOIN " + _enteteBa + " e ON e." + ColNoBa + " = l." + ColNoBa
        + " WHERE e." + ColNoDossierBa + " = ? AND l." + ColTypeLigne + " = '" + LigneReglementBa + "'"
        + " AND l." + ColDateReglement + " >= ? AND l." + ColDateReglement + " < ?"
        + " ORDER BY e." + ColNoBa + ", l." + ColNoLignePv;

    /// <summary>
    /// Requête des RÉGIMES de TVA source (catalogue global, code BRUT + libellé) + occurrences observées
    /// sur <c>ligne_pv</c>. Aucune borne (catalogue partagé par les études). LECTURE SEULE.
    /// </summary>
    public string SelectTaxRegimesSql =>
        "SELECT r." + ColCodeRegimeTva + " AS " + ColCodeRegime + ", r." + ColRegimeLibelle + " AS " + ColLibelleAlias
        + ", COUNT(lp." + ColCodeRegimeTva + ") AS " + ColRegimeOccurrences
        + " FROM " + _regimes + " r"
        + " LEFT JOIN " + _lignePv + " lp ON lp." + ColCodeRegimeTva + " = r." + ColCodeRegimeTva
        + " GROUP BY r." + ColCodeRegimeTva + ", r." + ColRegimeLibelle
        + " ORDER BY r." + ColCodeRegimeTva;

    /// <summary>Requête de comptage rapide (présence + accessibilité) d'une table — LECTURE SEULE.</summary>
    /// <param name="table">Nom de table QUALIFIÉ (issu de <see cref="ExpectedTables"/>, jamais une entrée utilisateur).</param>
    /// <returns>Le <c>SELECT COUNT(*)</c> correspondant.</returns>
    public static string CountSql(string table) => "SELECT COUNT(*) FROM " + table;

    /// <summary>
    /// Garde de LECTURE SEULE STRICTE (CLAUDE.md n°5, F01-F02 R1) : rejette toute commande qui n'est pas un
    /// <c>SELECT</c>. Défense en profondeur — toutes les requêtes sont des constantes SELECT.
    /// </summary>
    /// <param name="sql">La requête à exécuter.</param>
    /// <exception cref="InvalidOperationException">Si <paramref name="sql"/> n'est pas un SELECT.</exception>
    public static void EnsureSelectOnly(string sql)
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
