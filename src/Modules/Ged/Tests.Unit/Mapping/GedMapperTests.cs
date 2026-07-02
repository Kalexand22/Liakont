namespace Liakont.Modules.Ged.Tests.Unit.Mapping;

using System;
using System.Collections.Generic;
using FluentAssertions;
using Liakont.Agent.Contracts.Ged;
using Liakont.Modules.Ged.Domain.Catalog;
using Liakont.Modules.Ged.Domain.Mapping;
using Xunit;

/// <summary>
/// Goldens du moteur de mapping GED (F19 §4.5, INV-GED-05, GED12). Prouve le mapping POSITIF (brut → axes /
/// entités / relations, interprétation d'un axe <c>number</c> en <b>decimal half-up</b> — règle 1) ET les cas de
/// <b>DÉFÉREMENT</b> obligatoires (profil absent / non validé, axe obligatoire non résolu, axe mono-valeur
/// ambigu, valeur source incompatible) — jamais deviner ni inventer (règles 2/3). Vocabulaire NEUTRE (aucun
/// littéral métier) : la généricité est prouvée par la configuration.
/// </summary>
public sealed class GedMapperTests
{
    [Fact]
    public void Maps_axes_entities_and_relations_from_a_validated_profile()
    {
        var profile = ValidatedProfile();
        var doc = new IngestedDocumentDto(
            sourceReference: "src-1",
            documentType: "typ_a",
            sourceFields: new Dictionary<string, string>
            {
                ["date_champ"] = "2026-03-15",
                ["montant_champ"] = "10.005",
                ["parent_champ"] = "P-42",
            },
            sourceAxes: new[] { new RawAxisHint("refs", new List<string> { "L1", "L2" }) },
            sourceEntities: new[] { new RawEntityHint("partenaire", "E-7", "Partenaire Sept") });

        var result = GedMapper.Map(profile, doc, Catalog());

        result.IsMapped.Should().BeTrue();
        var mapped = result.Document!;
        mapped.DocumentType.Should().Be("typ_a");
        mapped.SourceReference.Should().Be("src-1");

        mapped.Axes.Should().Contain(a => a.AxisCode == "axe_date" && a.Value.ValueDate == new DateOnly(2026, 3, 15));

        // Interprétation d'un axe number en decimal half-up (n°1) : 10.005 arrondi à l'échelle 2 → 10.01,
        // sur la colonne typée decimal (ValueNumber), jamais un double/float.
        var montant = mapped.Axes.Should().ContainSingle(a => a.AxisCode == "axe_montant").Subject;
        montant.Value.ValueNumber.Should().Be(10.01m);

        // Axe multi-valeur : deux liens depuis le même sélecteur.
        var refs = new List<string>();
        foreach (var a in mapped.Axes)
        {
            if (a.AxisCode == "axe_ref")
            {
                refs.Add(a.Value.ValueString!);
            }
        }

        refs.Should().Equal("L1", "L2");

        mapped.Entities.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { EntityType = "ent_partenaire", ExternalId = "E-7", Display = "Partenaire Sept" });

        mapped.Relations.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Kind = "concerne", TargetType = "ent_parent", TargetExternalId = "P-42" });
    }

    [Fact]
    public void Defers_when_no_profile_exists_for_the_document_type()
    {
        var doc = Doc();

        var result = GedMapper.Map(profile: null, doc, Catalog());

        result.IsDeferred.Should().BeTrue();
        result.Document.Should().BeNull();
        result.DeferReason.Should().Contain("Aucun profil");
    }

    [Fact]
    public void Defers_when_the_profile_is_not_validated()
    {
        var profile = ValidatedProfile(validatedBy: null, validatedDate: null);
        var doc = Doc();

        var result = GedMapper.Map(profile, doc, Catalog());

        result.IsDeferred.Should().BeTrue();
        result.DeferReason.Should().Contain("n'est pas validé");
    }

    [Fact]
    public void Defers_when_a_required_axis_is_unresolved()
    {
        var profile = ValidatedProfile();
        var doc = new IngestedDocumentDto(
            sourceReference: "src-2",
            documentType: "typ_a",
            sourceFields: new Dictionary<string, string> { ["montant_champ"] = "12.00" });

        var result = GedMapper.Map(profile, doc, Catalog());

        result.IsDeferred.Should().BeTrue();
        result.DeferReason.Should().Contain("OBLIGATOIRE").And.Contain("axe_date");
    }

    [Fact]
    public void Defers_when_a_single_valued_axis_selector_is_ambiguous()
    {
        // axe_date est mono-valeur ; on rend deux valeurs par le sélecteur d'un champ multi → ambigu.
        var profile = GedMappingProfile.Create(
            "typ_a",
            GedMappingProfile.InitialProfileVersion,
            storagePolicy: null,
            validatedBy: "ec@example.test",
            validatedDate: new DateOnly(2026, 1, 1),
            axisRules: new[] { new AxisMappingRule("axe_ref", "$.axes[?name=='refs'].values[*]", IsRequired: true, IsMulti: false) },
            entityRules: Array.Empty<EntityMappingRule>(),
            relationRules: Array.Empty<RelationMappingRule>(),
            createdAt: DateTimeOffset.UnixEpoch);
        var doc = new IngestedDocumentDto(
            sourceReference: "src-3",
            documentType: "typ_a",
            sourceAxes: new[] { new RawAxisHint("refs", new List<string> { "L1", "L2" }) });

        var result = GedMapper.Map(profile, doc, Catalog());

        result.IsDeferred.Should().BeTrue();
        result.DeferReason.Should().Contain("ambigu");
    }

    [Fact]
    public void Defers_when_a_source_value_is_incompatible_with_the_axis_type()
    {
        var profile = ValidatedProfile();
        var doc = new IngestedDocumentDto(
            sourceReference: "src-4",
            documentType: "typ_a",
            sourceFields: new Dictionary<string, string> { ["date_champ"] = "31/12/2026" }); // non ISO

        var result = GedMapper.Map(profile, doc, Catalog());

        result.IsDeferred.Should().BeTrue();
        result.DeferReason.Should().Contain("incompatible");
    }

    [Fact]
    public void Defers_when_an_axis_is_unknown_to_the_catalog()
    {
        var profile = GedMappingProfile.Create(
            "typ_a",
            GedMappingProfile.InitialProfileVersion,
            storagePolicy: null,
            validatedBy: "ec@example.test",
            validatedDate: new DateOnly(2026, 1, 1),
            axisRules: new[] { new AxisMappingRule("axe_inconnu", "$.fields.x", IsRequired: false, IsMulti: false) },
            entityRules: Array.Empty<EntityMappingRule>(),
            relationRules: Array.Empty<RelationMappingRule>(),
            createdAt: DateTimeOffset.UnixEpoch);
        var doc = new IngestedDocumentDto(
            sourceReference: "src-5",
            documentType: "typ_a",
            sourceFields: new Dictionary<string, string> { ["x"] = "v" });

        var result = GedMapper.Map(profile, doc, EmptyCatalog.Instance);

        result.IsDeferred.Should().BeTrue();
        result.DeferReason.Should().Contain("inconnu");
    }

    [Fact]
    public void Optional_unresolved_axis_is_simply_skipped_not_deferred()
    {
        var profile = ValidatedProfile();
        var doc = new IngestedDocumentDto(
            sourceReference: "src-6",
            documentType: "typ_a",
            sourceFields: new Dictionary<string, string> { ["date_champ"] = "2026-03-15" }); // montant (optionnel) absent

        var result = GedMapper.Map(profile, doc, Catalog());

        result.IsMapped.Should().BeTrue();
        result.Document!.Axes.Should().NotContain(a => a.AxisCode == "axe_montant");
        result.Document!.Axes.Should().Contain(a => a.AxisCode == "axe_date");
    }

    [Fact]
    public void Defers_when_the_profile_declares_multi_but_the_catalog_declares_mono()
    {
        // GDF04 (1) : le profil déclare axe_date MULTI, mais le catalogue le déclare MONO. Deux valeurs ne
        // peuvent être rangées sans écrasement silencieux (le chemin d'écriture supersède la courante) → on
        // DÉFÈRE (INV-GED-05), jamais un dernier-gagnant arbitraire.
        var profile = GedMappingProfile.Create(
            "typ_a",
            GedMappingProfile.InitialProfileVersion,
            storagePolicy: null,
            validatedBy: "ec@example.test",
            validatedDate: new DateOnly(2026, 1, 1),
            axisRules: new[] { new AxisMappingRule("axe_date", "$.axes[?name=='dates'].values[*]", IsRequired: false, IsMulti: true) },
            entityRules: Array.Empty<EntityMappingRule>(),
            relationRules: Array.Empty<RelationMappingRule>(),
            createdAt: DateTimeOffset.UnixEpoch);
        var doc = new IngestedDocumentDto(
            sourceReference: "src-7",
            documentType: "typ_a",
            sourceAxes: new[] { new RawAxisHint("dates", new List<string> { "2026-01-01", "2026-02-02" }) });

        var result = GedMapper.Map(profile, doc, Catalog());

        result.IsDeferred.Should().BeTrue();
        result.Document.Should().BeNull();
        result.DeferReason.Should().Contain("MULTI-valeur par le profil mais MONO-valeur par le catalogue");
    }

    [Fact]
    public void Deduplicates_identical_values_on_a_multi_valued_axis()
    {
        // GDF04 (3) : axe_ref est multi-valeur au catalogue ; le sélecteur renvoie un doublon strict (L1, L1, L2).
        // Un lien courant strictement identique ne doit PAS être créé deux fois (index append-only, V009 sans
        // contrainte d'unicité) — sinon facette/fiche en double, jamais corrigeable.
        var profile = GedMappingProfile.Create(
            "typ_a",
            GedMappingProfile.InitialProfileVersion,
            storagePolicy: null,
            validatedBy: "ec@example.test",
            validatedDate: new DateOnly(2026, 1, 1),
            axisRules: new[] { new AxisMappingRule("axe_ref", "$.axes[?name=='refs'].values[*]", IsRequired: false, IsMulti: true) },
            entityRules: Array.Empty<EntityMappingRule>(),
            relationRules: Array.Empty<RelationMappingRule>(),
            createdAt: DateTimeOffset.UnixEpoch);
        var doc = new IngestedDocumentDto(
            sourceReference: "src-8",
            documentType: "typ_a",
            sourceAxes: new[] { new RawAxisHint("refs", new List<string> { "L1", "L1", "L2" }) });

        var result = GedMapper.Map(profile, doc, Catalog());

        result.IsMapped.Should().BeTrue();
        var refs = new List<string>();
        foreach (var a in result.Document!.Axes)
        {
            if (a.AxisCode == "axe_ref")
            {
                refs.Add(a.Value.ValueString!);
            }
        }

        refs.Should().Equal("L1", "L2");
    }

    [Fact]
    public void Pairs_entity_labels_positionally_when_counts_match()
    {
        // GDF04 (2) : deux entités, deux libellés (M==N) → appariement POSITIONNEL (E-1↔Un, E-2↔Deux),
        // jamais le libellé d'une autre entité.
        var profile = EntityOnlyProfile();
        var doc = new IngestedDocumentDto(
            sourceReference: "src-9",
            documentType: "typ_a",
            sourceEntities: new[]
            {
                new RawEntityHint("partenaire", "E-1", "Un"),
                new RawEntityHint("partenaire", "E-2", "Deux"),
            });

        var result = GedMapper.Map(profile, doc, Catalog());

        result.IsMapped.Should().BeTrue();
        result.Document!.Entities.Should().BeEquivalentTo(new[]
        {
            new { EntityType = "ent_partenaire", ExternalId = "E-1", Display = "Un" },
            new { EntityType = "ent_partenaire", ExternalId = "E-2", Display = "Deux" },
        });
    }

    [Fact]
    public void Does_not_borrow_another_entitys_label_when_the_label_count_differs()
    {
        // GDF04 (2) : deux identifiants, un seul libellé source (M==1, N==2) → le libellé n'est JAMAIS collé aux
        // deux (ce serait le displayName d'une autre entité) ; comportement défini = libellé absent partout.
        var profile = EntityOnlyProfile();
        var doc = new IngestedDocumentDto(
            sourceReference: "src-10",
            documentType: "typ_a",
            sourceEntities: new[]
            {
                new RawEntityHint("partenaire", "E-1", "Un"),
                new RawEntityHint("partenaire", "E-2"), // pas de libellé source
            });

        var result = GedMapper.Map(profile, doc, Catalog());

        result.IsMapped.Should().BeTrue();
        result.Document!.Entities.Should().HaveCount(2);
        result.Document!.Entities.Should().OnlyContain(e => e.Display == null);
        result.Document!.Entities.Should().Contain(e => e.ExternalId == "E-1")
            .And.Contain(e => e.ExternalId == "E-2");
    }

    private static GedMappingProfile ValidatedProfile(
        string? validatedBy = "ec@example.test",
        DateOnly? validatedDate = null)
    {
        return GedMappingProfile.Create(
            "typ_a",
            GedMappingProfile.InitialProfileVersion,
            storagePolicy: "WormPlusIndex",
            validatedBy: validatedBy,
            validatedDate: validatedBy is null ? null : validatedDate ?? new DateOnly(2026, 1, 1),
            axisRules: new[]
            {
                new AxisMappingRule("axe_date", "$.fields.date_champ", IsRequired: true, IsMulti: false),
                new AxisMappingRule("axe_montant", "$.fields.montant_champ", IsRequired: false, IsMulti: false),
                new AxisMappingRule("axe_ref", "$.axes[?name=='refs'].values[*]", IsRequired: false, IsMulti: true),
            },
            entityRules: new[]
            {
                new EntityMappingRule("ent_partenaire", "$.entities[?type=='partenaire'].externalId", "$.entities[?type=='partenaire'].display"),
            },
            relationRules: new[]
            {
                new RelationMappingRule("concerne", "ent_parent", "$.fields.parent_champ"),
            },
            createdAt: DateTimeOffset.UnixEpoch);
    }

    // Profil validé SANS axe, avec une seule règle d'entité (libellé apparié positionnellement) — pour isoler
    // le comportement de MapEntities (GDF04, appariement libellé↔identifiant).
    private static GedMappingProfile EntityOnlyProfile() => GedMappingProfile.Create(
        "typ_a",
        GedMappingProfile.InitialProfileVersion,
        storagePolicy: null,
        validatedBy: "ec@example.test",
        validatedDate: new DateOnly(2026, 1, 1),
        axisRules: Array.Empty<AxisMappingRule>(),
        entityRules: new[]
        {
            new EntityMappingRule("ent_partenaire", "$.entities[?type=='partenaire'].externalId", "$.entities[?type=='partenaire'].display"),
        },
        relationRules: Array.Empty<RelationMappingRule>(),
        createdAt: DateTimeOffset.UnixEpoch);

    private static IngestedDocumentDto Doc() => new(
        sourceReference: "src-0",
        documentType: "typ_a",
        sourceFields: new Dictionary<string, string> { ["date_champ"] = "2026-03-15" });

    private static InMemoryAxisCatalog Catalog() => new(new Dictionary<string, AxisMappingTarget>
    {
        ["axe_date"] = new AxisMappingTarget("axe_date", AxisDataType.Date, ValueScale: null, IsMultiValue: false),
        ["axe_montant"] = new AxisMappingTarget("axe_montant", AxisDataType.Number, ValueScale: 2, IsMultiValue: false),
        ["axe_ref"] = new AxisMappingTarget("axe_ref", AxisDataType.Text, ValueScale: null, IsMultiValue: true),
    });

    private sealed class InMemoryAxisCatalog : IAxisMappingCatalog
    {
        private readonly IReadOnlyDictionary<string, AxisMappingTarget> _axes;

        public InMemoryAxisCatalog(IReadOnlyDictionary<string, AxisMappingTarget> axes) => _axes = axes;

        public AxisMappingTarget? Resolve(string axisCode) => _axes.TryGetValue(axisCode, out var t) ? t : null;
    }

    private sealed class EmptyCatalog : IAxisMappingCatalog
    {
        public static readonly EmptyCatalog Instance = new();

        public AxisMappingTarget? Resolve(string axisCode) => null;
    }
}
