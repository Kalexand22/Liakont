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
/// Extracteur EncheresV6 rejouant des fixtures JSON au format source (<c>entete_ba</c> /
/// <c>lignes_ba</c> / <c>Regime_tva</c>) transformées en documents pivot par
/// <see cref="EncheresV6RowMapper"/> (F01-F02 §4.4). C'est le mode DEV sans licence Pervasive/Zen,
/// la démo hors site et le support des tests. La transformation fixture → pivot est STRICTEMENT
/// celle qu'utilisera le futur PervasiveExtractor (ODBC réel, ADP02) : seule la source des lignes
/// diffère. Comme tout extracteur, il n'a AUCUNE logique métier (CLAUDE.md n°6) et n'écrit/ne
/// verrouille rien — il rejoue des fichiers (R1, lecture seule par construction).
/// </summary>
public sealed class EncheresV6FixtureExtractor : IExtractor
{
    private readonly EncheresV6EmitterIdentity _emitter;
    private readonly OperationCategory _operationCategory;
    private readonly List<EncheresV6Bordereau> _bordereaux;
    private readonly List<EncheresV6Regime> _regimes;
    private readonly Dictionary<string, EncheresV6Bordereau> _byNoBa;
    private readonly IEncheresV6PdfSource _pdfSource;

    private EncheresV6FixtureExtractor(
        EncheresV6SourceSnapshot snapshot,
        EncheresV6EmitterIdentity emitter,
        OperationCategory operationCategory,
        IEncheresV6PdfSource? pdfSource)
    {
        _emitter = emitter ?? throw new ArgumentNullException(nameof(emitter));
        _operationCategory = operationCategory;
        _bordereaux = snapshot.Bordereaux;
        _regimes = snapshot.Regimes;
        _byNoBa = IndexByNoBa(_bordereaux);

        // Source PDF (ADP05) : la capacité PDF est PORTÉE PAR LA CONFIG (la source des PDF est la même que
        // les documents viennent des fixtures ou de l'ODBC). Sans config PDF, null-object → capacités false.
        _pdfSource = pdfSource ?? NullEncheresV6PdfSource.Instance;
        Capabilities = new ExtractorCapabilities(
            providesSourceDocuments: _pdfSource.ProvidesSourceDocuments,
            providesUnlinkedDocumentPool: _pdfSource.ProvidesUnlinkedDocumentPool,
            hasDetailedLines: true,
            hasCreditNoteLink: true,
            exposesPayments: true,
            regimeKeyShape: RegimeKeyShape.Simple,
            emitterIdentitySource: EmitterIdentitySource.FromConfig,
            hasStoredHeaderTotal: true,
            isMutableAfterIssue: false,
            numberUniquenessScope: NumberUniquenessScope.Global);
    }

    /// <inheritdoc />
    public string SourceName => "EncheresV6";

    /// <inheritdoc />
    public ExtractorCapabilities Capabilities { get; }

    /// <summary>Construit un extracteur depuis un contenu JSON de fixtures EncheresV6.</summary>
    /// <param name="json">Le contenu JSON (régimes + bordereaux).</param>
    /// <param name="emitter">Identité de l'émetteur (paramétrage tenant).</param>
    /// <param name="operationCategory">Nature d'opération de la source (paramétrage — F01-F02 §7 #3).</param>
    /// <param name="pdfSource">Source des PDF de bordereaux (ADP05). <c>null</c> ⇒ aucune capacité PDF déclarée.</param>
    /// <returns>L'extracteur de fixtures correspondant.</returns>
    public static EncheresV6FixtureExtractor FromJson(
        string json,
        EncheresV6EmitterIdentity emitter,
        OperationCategory operationCategory,
        IEncheresV6PdfSource? pdfSource = null)
    {
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        return new EncheresV6FixtureExtractor(Deserialize(json), emitter, operationCategory, pdfSource);
    }

    /// <summary>Construit un extracteur depuis un fichier de fixtures EncheresV6.</summary>
    /// <param name="path">Chemin du fichier JSON.</param>
    /// <param name="emitter">Identité de l'émetteur (paramétrage tenant).</param>
    /// <param name="operationCategory">Nature d'opération de la source (paramétrage — F01-F02 §7 #3).</param>
    /// <param name="pdfSource">Source des PDF de bordereaux (ADP05). <c>null</c> ⇒ aucune capacité PDF déclarée.</param>
    /// <returns>L'extracteur de fixtures correspondant.</returns>
    public static EncheresV6FixtureExtractor FromFile(
        string path,
        EncheresV6EmitterIdentity emitter,
        OperationCategory operationCategory,
        IEncheresV6PdfSource? pdfSource = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Le chemin du fichier de fixtures est requis.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new SourceSchemaException($"Le fichier de fixtures EncheresV6 est introuvable : « {path} ».");
        }

        return FromJson(File.ReadAllText(path), emitter, operationCategory, pdfSource);
    }

    /// <summary>
    /// Construit un extracteur en fusionnant tous les fichiers <c>*.json</c> d'un répertoire de
    /// fixtures (régimes et bordereaux concaténés). Permet d'organiser les cas en plusieurs fichiers.
    /// </summary>
    /// <param name="directory">Répertoire des fixtures EncheresV6.</param>
    /// <param name="emitter">Identité de l'émetteur (paramétrage tenant).</param>
    /// <param name="operationCategory">Nature d'opération de la source (paramétrage — F01-F02 §7 #3).</param>
    /// <param name="pdfSource">Source des PDF de bordereaux (ADP05). <c>null</c> ⇒ aucune capacité PDF déclarée.</param>
    /// <returns>L'extracteur de fixtures correspondant.</returns>
    public static EncheresV6FixtureExtractor FromDirectory(
        string directory,
        EncheresV6EmitterIdentity emitter,
        OperationCategory operationCategory,
        IEncheresV6PdfSource? pdfSource = null)
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
        }

        return new EncheresV6FixtureExtractor(merged, emitter, operationCategory, pdfSource);
    }

    /// <inheritdoc />
    public ExtractorInfo GetInfo() =>
        new ExtractorInfo("EncheresV6", "1.0.0-fixture", "Magic XPA / Pervasive (mode fixtures)");

    /// <inheritdoc />
    public HealthCheckResult CheckHealth() =>
        HealthCheckResult.Healthy(
            $"Source de fixtures EncheresV6 : {_bordereaux.Count} bordereau(x), {_regimes.Count} régime(s).");

    /// <inheritdoc />
    public IEnumerable<PivotDocumentDto> ExtractDocuments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc)
    {
        // SIMPLIFICATION MODE FIXTURES : filtrage sur la date_vente (date métier).
        // Acceptable ici car le rejeu est statique et idempotent (pas de watermark avançant).
        // Le futur PervasiveExtractor (ADP02) DOIT filtrer sur un timestamp monotone
        // d'insertion/modification (ou une fenêtre de récupération) conformément au contrat
        // IExtractor « DISPONIBLE DEPUIS » — un document antidaté ou saisi tardivement doit
        // rester extractable après l'avancement du watermark.
        foreach (EncheresV6Bordereau bordereau in _bordereaux)
        {
            if (!IsInPeriod(bordereau.DateVente, fromInclusiveUtc, toExclusiveUtc))
            {
                continue;
            }

            EncheresV6Bordereau? origin = ResolveCreditNoteOrigin(bordereau);
            yield return EncheresV6RowMapper.MapDocument(bordereau, _emitter, _operationCategory, origin);
        }
    }

    /// <inheritdoc />
    public IEnumerable<PivotPaymentDto> ExtractPayments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc)
    {
        // SIMPLIFICATION MODE FIXTURES : filtrage sur la date_reglement (date métier).
        // Le futur PervasiveExtractor (ADP02) doit filtrer sur un timestamp monotone.
        foreach (EncheresV6Bordereau bordereau in _bordereaux)
        {
            foreach (EncheresV6Ligne ligne in bordereau.Lignes)
            {
                if (EncheresV6RowMapper.IsPaymentLine(ligne.TypeLigne)
                    && ligne.DateReglement.HasValue
                    && IsInPeriod(ligne.DateReglement.Value, fromInclusiveUtc, toExclusiveUtc))
                {
                    yield return EncheresV6RowMapper.MapPayment(bordereau, ligne);
                }
            }
        }
    }

    /// <summary>
    /// Extrait les FRAIS VENDEUR (bordereau vendeur, BV) d'une période depuis les fixtures (F01-F02 §4.3.1,
    /// B2C-07). Mêmes sémantique et mapping (parité) que le <see cref="PervasiveExtractor"/> : lignes
    /// <c>type_ligne = "5"</c> rattachées à leur bordereau par <c>no_ba</c> (option (a), B2C-06), converties
    /// par <see cref="EncheresV6RowMapper.MapSellerFee"/>. EXTRACTION PURE : aucune logique fiscale (R3).
    /// </summary>
    /// <param name="fromInclusiveUtc">Borne basse de la période (UTC, incluse).</param>
    /// <param name="toExclusiveUtc">Borne haute de la période (UTC, exclue).</param>
    /// <returns>Les frais vendeur de la période, rattachés à leur bordereau (par <c>no_ba</c>).</returns>
    public IReadOnlyList<EncheresV6SellerFee> ExtractSellerFees(DateTime fromInclusiveUtc, DateTime toExclusiveUtc)
    {
        // SIMPLIFICATION MODE FIXTURES : filtrage sur la date_vente du bordereau (date métier) — même axe
        // que ExtractDocuments. Le frais vendeur (type 5) est une ligne du même bordereau (option (a), B2C-06).
        var fees = new List<EncheresV6SellerFee>();
        foreach (EncheresV6Bordereau bordereau in _bordereaux)
        {
            if (!IsInPeriod(bordereau.DateVente, fromInclusiveUtc, toExclusiveUtc))
            {
                continue;
            }

            foreach (EncheresV6Ligne ligne in bordereau.Lignes)
            {
                if (EncheresV6RowMapper.IsSellerFeeLine(ligne.TypeLigne))
                {
                    fees.Add(EncheresV6RowMapper.MapSellerFee(bordereau, ligne));
                }
            }
        }

        return fees;
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

        // Dédup par code_regime : on retient la dernière déclaration (last-wins) pour le libellé,
        // tout en préservant l'ordre d'apparition du premier code (stable, déterministe).
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
    public IReadOnlyList<SourceAttachment> GetAttachments(string sourceReference)
    {
        // Délégation à la source PDF configurée (ADP05) : dossier de fichiers, ou null-object (vide) sans config.
        return _pdfSource.GetAttachments(sourceReference);
    }

    /// <inheritdoc />
    public IEnumerable<PoolDocument> ListPoolDocuments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc) =>
        _pdfSource.ListPoolDocuments(fromInclusiveUtc, toExclusiveUtc);

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

    private static Dictionary<string, EncheresV6Bordereau> IndexByNoBa(IEnumerable<EncheresV6Bordereau> bordereaux)
    {
        var index = new Dictionary<string, EncheresV6Bordereau>(StringComparer.Ordinal);
        foreach (EncheresV6Bordereau bordereau in bordereaux)
        {
            if (string.IsNullOrWhiteSpace(bordereau.NoBa))
            {
                continue;
            }

            if (index.ContainsKey(bordereau.NoBa!))
            {
                throw new SourceSchemaException(
                    $"Fixtures EncheresV6 : no_ba « {bordereau.NoBa} » dupliqué. Chaque bordereau doit avoir une référence source unique (idempotence R2).");
            }

            index[bordereau.NoBa!] = bordereau;
        }

        return index;
    }

    private static bool IsInPeriod(DateTime value, DateTime fromInclusive, DateTime toExclusive) =>
        value >= fromInclusive && value < toExclusive;

    private EncheresV6Bordereau? ResolveCreditNoteOrigin(EncheresV6Bordereau bordereau)
    {
        if (!string.Equals(bordereau.BordereauOuAvoir, EncheresV6RowMapper.PieceAvoir, StringComparison.Ordinal))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(bordereau.NoBaLettrage))
        {
            // Origine non renseignée : on laisse le mapper bloquer l'avoir (ADR-0004 D3-3, jamais deviner).
            return null;
        }

        _byNoBa.TryGetValue(bordereau.NoBaLettrage!, out EncheresV6Bordereau? origin);
        return origin;
    }
}
