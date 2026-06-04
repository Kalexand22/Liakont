namespace Liakont.Modules.Reconciliation.Domain;

using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Moteur PUR de rapprochement PDF ↔ document émis (item TRK07). Aucune E/S : il reçoit un
/// PDF du pool (nom + texte déjà extrait) et la liste des documents émis candidats, et produit une
/// <see cref="ReconciliationDecision"/>. Les trois stratégies (TRK07), dans l'ordre de confiance :
/// <list type="number">
///   <item>numéro de document dans le NOM DE FICHIER (confiance HAUTE) ;</item>
///   <item>numéro de document dans le TEXTE du PDF (confiance HAUTE) ;</item>
///   <item>date d'émission + montant TTC présents, candidat UNIQUE (confiance MOYENNE).</item>
/// </list>
/// Règle d'or (TRK07, décision 2026-06-02) : AMBIGUÏTÉ (≥ 2 candidats) ou absence de correspondance ⇒ NON
/// RÉCONCILIÉ. Jamais de lien automatique en dessous de la confiance haute (un rapprochement erroné
/// archivé en WORM serait incorrigible).
/// </summary>
public static class ReconciliationEngine
{
    public static ReconciliationDecision Decide(PooledPdfContent pdf, IReadOnlyList<DocumentCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(candidates);

        // Stratégies 1 + 2 (confiance HAUTE) : numéro de document dans le nom de fichier OU le texte.
        var highMatches = new List<(DocumentCandidate Candidate, MatchStrategy Strategy)>();
        var seenHigh = new HashSet<Guid>();
        foreach (DocumentCandidate candidate in candidates)
        {
            bool inFileName = DocumentNumberMatcher.Contains(pdf.FileName, candidate.DocumentNumber);
            bool inContent = DocumentNumberMatcher.Contains(pdf.ExtractedText, candidate.DocumentNumber);
            if ((inFileName || inContent) && seenHigh.Add(candidate.DocumentId))
            {
                highMatches.Add((candidate, inFileName ? MatchStrategy.FileName : MatchStrategy.PdfContent));
            }
        }

        if (highMatches.Count == 1)
        {
            (DocumentCandidate candidate, MatchStrategy strategy) = highMatches[0];
            string where = strategy == MatchStrategy.FileName ? "le nom de fichier" : "le texte du PDF";
            return ReconciliationDecision.AutoLinked(
                candidate.DocumentId,
                strategy,
                $"Numéro de document « {candidate.DocumentNumber} » trouvé dans {where} (confiance haute, TRK07).");
        }

        if (highMatches.Count >= 2)
        {
            return ReconciliationDecision.NotReconciled(
                $"Ambiguïté : {highMatches.Count} documents portent un numéro présent dans le PDF — rapprochement automatique refusé (TRK07).");
        }

        // Stratégie 3 (confiance MOYENNE) : date d'émission ET montant TTC présents, candidat UNIQUE.
        string haystack = pdf.FileName + "\n" + (pdf.ExtractedText ?? string.Empty);
        var mediumMatches = new List<DocumentCandidate>();
        var seenMedium = new HashSet<Guid>();
        foreach (DocumentCandidate candidate in candidates)
        {
            if (DateAppears(haystack, candidate.IssueDate)
                && AmountAppears(haystack, candidate.TotalGross)
                && seenMedium.Add(candidate.DocumentId))
            {
                mediumMatches.Add(candidate);
            }
        }

        if (mediumMatches.Count == 1)
        {
            DocumentCandidate candidate = mediumMatches[0];
            string reason = $"Correspondance date ({candidate.IssueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}) "
                + $"+ montant TTC ({candidate.TotalGross.ToString("0.00", CultureInfo.InvariantCulture)}) — "
                + "candidat unique, confirmation opérateur requise (TRK07).";
            return ReconciliationDecision.ProposeManual(candidate.DocumentId, reason);
        }

        if (mediumMatches.Count >= 2)
        {
            return ReconciliationDecision.NotReconciled(
                $"Ambiguïté : {mediumMatches.Count} documents correspondent par date et montant — aucun rapprochement automatique (TRK07).");
        }

        return ReconciliationDecision.NotReconciled("Aucune correspondance avec un document émis (orphelin, file d'attente manuelle).");
    }

    private static bool DateAppears(string haystack, DateOnly issueDate)
    {
        ReadOnlySpan<string> formats = ["yyyy-MM-dd", "dd/MM/yyyy", "dd-MM-yyyy", "dd.MM.yyyy"];
        foreach (string format in formats)
        {
            if (haystack.Contains(issueDate.ToString(format, CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AmountAppears(string haystack, decimal totalGross)
    {
        // Montant à 2 décimales (decimal, jamais flottant — CLAUDE.md n°1) : forme invariante « 1162.80 »
        // et forme française « 1162,80 ». La stratégie reste de confiance MOYENNE (confirmation requise),
        // donc une non-correspondance (ex. séparateur de milliers) bascule en orphelin, sans risque fiscal.
        string invariant = totalGross.ToString("0.00", CultureInfo.InvariantCulture);
        string french = invariant.Replace('.', ',');
        return haystack.Contains(invariant, StringComparison.Ordinal)
            || haystack.Contains(french, StringComparison.Ordinal);
    }
}
