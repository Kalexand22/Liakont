namespace Liakont.Agent.Adapters.EncheresV6;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Liakont.Agent.Adapters.EncheresV6.Source;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Transport;
using Liakont.Agent.Core;
using Liakont.Agent.Core.Extraction;
using Newtonsoft.Json;

/// <summary>
/// Extracteur EncheresV6 rejouant des fixtures JSON (bordereaux ACHETEUR + VENDEUR + régimes) transformées
/// par <see cref="EncheresV6RowMapper"/> — mode DEV sans licence, démo hors site, tests. La transformation
/// fixture → pivot est STRICTEMENT celle du <see cref="PervasiveExtractor"/> (ODBC) : seule la source des
/// données diffère. Aucune logique métier (CLAUDE.md n°6) ; lecture seule par construction (R1).
/// </summary>
public sealed class EncheresV6FixtureExtractor : IExtractor
{
    private readonly List<EncheresV6Bordereau> _bordereaux;
    private readonly List<EncheresV6BordereauVendeur> _bordereauxVendeur;
    private readonly List<EncheresV6FactureClient> _facturesClients;
    private readonly List<EncheresV6Regime> _regimes;
    private readonly Dictionary<string, EncheresV6Bordereau> _baByNo;
    private readonly Dictionary<string, EncheresV6BordereauVendeur> _bvByNo;
    private readonly Dictionary<string, EncheresV6FactureClient> _factureByNo;

    private EncheresV6FixtureExtractor(EncheresV6SourceSnapshot snapshot)
    {
        _bordereaux = snapshot.Bordereaux;
        _bordereauxVendeur = snapshot.BordereauxVendeur;
        _facturesClients = snapshot.FacturesClients;
        _regimes = snapshot.Regimes;
        _baByNo = IndexBa(_bordereaux);
        _bvByNo = IndexBv(_bordereauxVendeur);
        _factureByNo = IndexFactures(_facturesClients);

        Capabilities = new ExtractorCapabilities(
            providesSourceDocuments: false,
            providesUnlinkedDocumentPool: false,
            hasDetailedLines: true,
            hasCreditNoteLink: true,
            exposesPayments: true,
            regimeKeyShape: RegimeKeyShape.Composite,
            emitterIdentitySource: EmitterIdentitySource.FilledByPlatform,
            hasStoredHeaderTotal: true,
            isMutableAfterIssue: false,
            numberUniquenessScope: NumberUniquenessScope.Global,
            extractsOnlyFinalizedDocuments: true);
    }

    /// <inheritdoc />
    public string SourceName => "EncheresV6";

    /// <inheritdoc />
    public ExtractorCapabilities Capabilities { get; }

    /// <summary>Construit un extracteur depuis un contenu JSON de fixtures.</summary>
    /// <param name="json">Le contenu JSON (régimes + bordereaux acheteur/vendeur).</param>
    /// <returns>L'extracteur de fixtures correspondant.</returns>
    public static EncheresV6FixtureExtractor FromJson(string json)
    {
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        return new EncheresV6FixtureExtractor(Deserialize(json));
    }

    /// <summary>Construit un extracteur depuis un fichier de fixtures.</summary>
    /// <param name="path">Chemin du fichier JSON.</param>
    /// <returns>L'extracteur de fixtures correspondant.</returns>
    public static EncheresV6FixtureExtractor FromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Le chemin du fichier de fixtures est requis.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new SourceSchemaException($"Le fichier de fixtures EncheresV6 est introuvable : « {path} ».");
        }

        return FromJson(File.ReadAllText(path));
    }

    /// <summary>Construit un extracteur en fusionnant tous les fichiers <c>*.json</c> d'un répertoire.</summary>
    /// <param name="directory">Répertoire des fixtures EncheresV6.</param>
    /// <returns>L'extracteur de fixtures correspondant.</returns>
    public static EncheresV6FixtureExtractor FromDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("Le répertoire de fixtures est requis.", nameof(directory));
        }

        if (!Directory.Exists(directory))
        {
            throw new SourceSchemaException($"Le répertoire de fixtures EncheresV6 est introuvable : « {directory} ».");
        }

        var merged = new EncheresV6SourceSnapshot();
        foreach (string file in Directory.EnumerateFiles(directory, "*.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            EncheresV6SourceSnapshot snapshot = Deserialize(File.ReadAllText(file));
            merged.Regimes.AddRange(snapshot.Regimes);
            merged.Bordereaux.AddRange(snapshot.Bordereaux);
            merged.BordereauxVendeur.AddRange(snapshot.BordereauxVendeur);
            merged.FacturesClients.AddRange(snapshot.FacturesClients);
        }

        return new EncheresV6FixtureExtractor(merged);
    }

    /// <inheritdoc />
    public ExtractorInfo GetInfo() =>
        new ExtractorInfo("EncheresV6", "2.0.0-fixture", "EncheresV6 (mode fixtures)");

    /// <inheritdoc />
    public HealthCheckResult CheckHealth() =>
        HealthCheckResult.Healthy(
            $"Source de fixtures EncheresV6 : {_bordereaux.Count} BA, {_bordereauxVendeur.Count} BV, "
            + $"{_facturesClients.Count} facture(s) client, {_regimes.Count} régime(s).");

    /// <inheritdoc />
    public IEnumerable<PivotDocumentDto> ExtractDocuments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc)
    {
        foreach (EncheresV6Bordereau bordereau in _bordereaux)
        {
            if (!IsInPeriod(bordereau.DateVente, fromInclusiveUtc, toExclusiveUtc))
            {
                continue;
            }

            yield return EncheresV6RowMapper.MapBaDocument(bordereau, ResolveBaOrigin(bordereau));
        }

        foreach (EncheresV6BordereauVendeur bordereau in _bordereauxVendeur)
        {
            if (!IsInPeriod(bordereau.DateVente, fromInclusiveUtc, toExclusiveUtc))
            {
                continue;
            }

            yield return EncheresV6RowMapper.MapBvDocument(bordereau, ResolveBvOrigin(bordereau));
        }

        foreach (EncheresV6FactureClient facture in _facturesClients)
        {
            if (!IsInPeriod(facture.DateFact, fromInclusiveUtc, toExclusiveUtc))
            {
                continue;
            }

            yield return EncheresV6RowMapper.MapFactureClientDocument(facture, ResolveFactureOrigin(facture));
        }
    }

    /// <inheritdoc />
    public IEnumerable<PivotPaymentDto> ExtractPayments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc)
    {
        foreach (EncheresV6Bordereau bordereau in _bordereaux)
        {
            foreach (EncheresV6Ligne ligne in bordereau.Lignes)
            {
                if (EncheresV6RowMapper.IsPaymentLineBa(ligne.TypeLigne)
                    && ligne.DateReglement.HasValue
                    && IsInPeriod(ligne.DateReglement.Value, fromInclusiveUtc, toExclusiveUtc))
                {
                    yield return EncheresV6RowMapper.MapPayment(bordereau, ligne);
                }
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<SourceTaxRegimeDto> ListSourceTaxRegimes()
    {
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (EncheresV6Ligne ligne in _bordereaux.SelectMany(b => b.Lignes))
        {
            if (!string.IsNullOrWhiteSpace(ligne.CodeRegime))
            {
                occurrences.TryGetValue(ligne.CodeRegime!, out int count);
                occurrences[ligne.CodeRegime!] = count + 1;
            }
        }

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var deduped = new List<EncheresV6Regime>();
        foreach (EncheresV6Regime r in _regimes)
        {
            if (string.IsNullOrWhiteSpace(r.CodeRegime))
            {
                continue;
            }

            if (seen.TryGetValue(r.CodeRegime!, out int idx))
            {
                deduped[idx] = r;
            }
            else
            {
                seen[r.CodeRegime!] = deduped.Count;
                deduped.Add(r);
            }
        }

        return deduped
            .Select(r => new SourceTaxRegimeDto(
                r.CodeRegime!,
                r.Libelle,
                occurrences.TryGetValue(r.CodeRegime!, out int count) ? count : 0))
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<SourceAttachment> GetAttachments(string sourceReference) => Array.Empty<SourceAttachment>();

    /// <inheritdoc />
    public IEnumerable<PoolDocument> ListPoolDocuments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc) =>
        Array.Empty<PoolDocument>();

    private static EncheresV6SourceSnapshot Deserialize(string json)
    {
        EncheresV6SourceSnapshot? snapshot;
        try
        {
            snapshot = JsonConvert.DeserializeObject<EncheresV6SourceSnapshot>(json);
        }
        catch (JsonException ex)
        {
            throw new SourceSchemaException(
                $"Fixtures EncheresV6 : JSON invalide ({ex.Message}). Corrigez la syntaxe puis relancez.",
                ex);
        }

        if (snapshot is null)
        {
            throw new SourceSchemaException(
                "Fixtures EncheresV6 : contenu vide. Renseignez au minimum « regimes » et « bordereaux ».");
        }

        return snapshot;
    }

    private static Dictionary<string, EncheresV6Bordereau> IndexBa(IEnumerable<EncheresV6Bordereau> bordereaux)
    {
        var index = new Dictionary<string, EncheresV6Bordereau>(StringComparer.Ordinal);
        foreach (EncheresV6Bordereau bordereau in bordereaux)
        {
            if (string.IsNullOrWhiteSpace(bordereau.NoBa))
            {
                continue;
            }

            // Idempotence (R2) : un no_ba dupliqué produirait deux pivots de même SourceReference
            // (double-déclaration). Le mode ODBC est protégé par la PK source ; en fixtures on bloque.
            if (index.ContainsKey(bordereau.NoBa!))
            {
                throw new SourceSchemaException(
                    $"Fixtures EncheresV6 : bordereau acheteur « no_ba={bordereau.NoBa} » dupliqué — "
                    + "chaque bordereau doit être unique (idempotence R2). Corrigez les fixtures.");
            }

            index[bordereau.NoBa!] = bordereau;
        }

        return index;
    }

    private static Dictionary<string, EncheresV6BordereauVendeur> IndexBv(IEnumerable<EncheresV6BordereauVendeur> bordereaux)
    {
        var index = new Dictionary<string, EncheresV6BordereauVendeur>(StringComparer.Ordinal);
        foreach (EncheresV6BordereauVendeur bordereau in bordereaux)
        {
            if (string.IsNullOrWhiteSpace(bordereau.NoBv))
            {
                continue;
            }

            // Idempotence (R2) : un no_bv dupliqué produirait deux pivots de même SourceReference.
            if (index.ContainsKey(bordereau.NoBv!))
            {
                throw new SourceSchemaException(
                    $"Fixtures EncheresV6 : bordereau vendeur « no_bv={bordereau.NoBv} » dupliqué — "
                    + "chaque bordereau doit être unique (idempotence R2). Corrigez les fixtures.");
            }

            index[bordereau.NoBv!] = bordereau;
        }

        return index;
    }

    private static Dictionary<string, EncheresV6FactureClient> IndexFactures(IEnumerable<EncheresV6FactureClient> factures)
    {
        var index = new Dictionary<string, EncheresV6FactureClient>(StringComparer.Ordinal);
        foreach (EncheresV6FactureClient facture in factures)
        {
            if (string.IsNullOrWhiteSpace(facture.NoFact))
            {
                continue;
            }

            // Idempotence (R2) : un no_fact dupliqué produirait deux pivots de même SourceReference.
            if (index.ContainsKey(facture.NoFact!))
            {
                throw new SourceSchemaException(
                    $"Fixtures EncheresV6 : facture client « no_fact={facture.NoFact} » dupliquée — "
                    + "chaque facture doit être unique (idempotence R2). Corrigez les fixtures.");
            }

            index[facture.NoFact!] = facture;
        }

        return index;
    }

    private static bool IsInPeriod(DateTime value, DateTime fromInclusive, DateTime toExclusive) =>
        value >= fromInclusive && value < toExclusive;

    private EncheresV6Bordereau? ResolveBaOrigin(EncheresV6Bordereau bordereau)
    {
        if (!string.Equals(bordereau.BordereauOuAvoir, EncheresV6RowMapper.PieceAvoir, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(bordereau.NoBaLettrage))
        {
            return null;
        }

        _baByNo.TryGetValue(bordereau.NoBaLettrage!, out EncheresV6Bordereau? origin);
        return origin;
    }

    private EncheresV6BordereauVendeur? ResolveBvOrigin(EncheresV6BordereauVendeur bordereau)
    {
        if (!string.Equals(bordereau.BordereauOuAvoir, EncheresV6RowMapper.PieceAvoir, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(bordereau.NoBvLettrage))
        {
            return null;
        }

        _bvByNo.TryGetValue(bordereau.NoBvLettrage!, out EncheresV6BordereauVendeur? origin);
        return origin;
    }

    private EncheresV6FactureClient? ResolveFactureOrigin(EncheresV6FactureClient facture)
    {
        if (!string.Equals(facture.FactureOuAvoir, EncheresV6RowMapper.PieceAvoir, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(facture.NoFactureLettrage))
        {
            return null;
        }

        _factureByNo.TryGetValue(facture.NoFactureLettrage!, out EncheresV6FactureClient? origin);
        return origin;
    }
}
