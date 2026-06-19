namespace Liakont.Modules.Pipeline.Tests.Unit;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Liakont.Agent.Contracts.ContractTests;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Serialization;
using Liakont.Modules.Pipeline.Infrastructure.Serialization;
using Xunit;

/// <summary>
/// Round-trip du lecteur canonique (INV-PIPELINE-001/002) : pour chaque golden de DOCUMENT UNIQUE de
/// <c>tests/fixtures/contrat-v1/</c>, <c>Serialize(Read(json)) == json</c> octet par octet. Les deux
/// enveloppes de transport (<c>batch-mixte.json</c>, <c>heartbeat.json</c>) ne sont PAS des
/// <c>PivotDocument</c> uniques : elles sont exclues.
/// </summary>
public sealed class PivotCanonicalJsonReaderTests
{
    private static readonly string[] NonDocumentFixtures = { "batch-mixte.json", "heartbeat.json" };

    public static IEnumerable<object[]> SingleDocumentGoldenFiles()
    {
        foreach (var path in Directory.EnumerateFiles(FixturesDirectory(), "*.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            if (!NonDocumentFixtures.Contains(name, StringComparer.Ordinal))
            {
                yield return new object[] { name };
            }
        }
    }

    [Theory]
    [MemberData(nameof(SingleDocumentGoldenFiles))]
    public void Round_Trip_Is_Byte_For_Byte_Stable(string fixtureFileName)
    {
        var json = File.ReadAllText(Path.Combine(FixturesDirectory(), fixtureFileName));

        var rebuilt = PivotCanonicalJsonReader.Read(json);

        CanonicalJson.Serialize(rebuilt).Should().Be(
            json,
            "désérialiser puis re-sérialiser le golden « {0} » doit être stable octet par octet (ADR-0007)",
            fixtureFileName);
    }

    [Fact]
    public void All_Eight_Single_Document_Goldens_Are_Covered()
    {
        // Garde anti-faux-vert : si un golden manque, le Theory paramétré ne couvrirait rien sans bruit.
        SingleDocumentGoldenFiles().Count().Should().BeGreaterThanOrEqualTo(
            8, "les 8 fixtures de document unique de tests/fixtures/contrat-v1/ doivent être couvertes");
    }

    [Fact]
    public void Decimal_Scale_Is_Preserved_On_Round_Trip()
    {
        var json = File.ReadAllText(Path.Combine(FixturesDirectory(), "facture-standard-b2c.json"));

        var rebuilt = PivotCanonicalJsonReader.Read(json);

        // 120.00m et 120m sont égaux en VALEUR : seule la re-sérialisation prouve la préservation d'échelle.
        CanonicalJson.Serialize(rebuilt).Should().Contain(
            "\"TotalNet\":120.00", "l'échelle décimale source (« 120.00 ») doit être préservée, jamais « 120 »");
    }

    [Fact]
    public void Payment_Due_Date_Survives_The_Round_Trip_For_The_Pa_Send()
    {
        // EXT01 — bout en bout : l'échéance (BT-9) écrite par l'agent dans le staging doit être RELUE par
        // le pipeline (ce lecteur) avant l'envoi à la PA. Sans cette lecture, le champ serait écrit puis
        // PERDU à la relecture → la facture non soldée resterait rejetée par BR-CO-25 malgré l'échéance.
        var pivot = new PivotDocumentDto(
            sourceDocumentKind: "FACTURE",
            number: "F-ECHEANCE",
            issueDate: new DateTime(2026, 1, 15),
            sourceReference: "SRC-F-ECHEANCE",
            supplier: new PivotPartyDto("SVV Démo", siren: "123456789"),
            totals: new PivotTotalsDto(100m, 20m, 120m),
            operationCategory: OperationCategory.LivraisonBiens,
            customer: new PivotPartyDto("Client Démo", siren: "987654321"),
            lines: new[] { new PivotLineDto("Prestation", 100m, taxes: new[] { new PivotLineTaxDto(20m, 20m, VatCategory.S) }) },
            paymentDueDate: new DateTime(2026, 2, 15));
        var json = CanonicalJson.Serialize(pivot);

        var rebuilt = PivotCanonicalJsonReader.Read(json);

        rebuilt.PaymentDueDate.Should().Be(new DateTime(2026, 2, 15), "BT-9 doit traverser le staging intacte");
        CanonicalJson.Serialize(rebuilt).Should().Be(json, "round-trip stable octet par octet avec BT-9 (ADR-0007)");
    }

    [Fact]
    public void Read_Null_Throws()
    {
        var act = () => PivotCanonicalJsonReader.Read(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// RDL02 — garde de COMPLÉTUDE PAR RÉFLEXION du lecteur de PRODUCTION : chaque propriété publique de
    /// chaque DTO pivot est CONSOMMÉE par <see cref="PivotCanonicalJsonReader"/>. ADR-0007 promettait
    /// « le lecteur ne vit PAS en prod » ; or ce lecteur sert le SEND (SendTenantJob), miroir au champ près
    /// du writer. Un champ porté par l'agent mais oublié ici serait AMPUTÉ avant transmission PA (EXT01/BT-9
    /// l'a frôlé ; ReasonCode/PaymentDueDate ne sont couverts par AUCUN golden). La réflexion force le
    /// document de référence à exercer chaque propriété ; le round-trip octet-par-octet prouve la consommation.
    /// </summary>
    [Fact]
    public void Reader_Consumes_Every_Public_Property_Of_Every_Pivot_Dto()
    {
        // Document de RÉFÉRENCE entièrement peuplé (source UNIQUE : ContractFixtures, RDL02). Chaque
        // optionnel et collection est renseigné, dont PaymentDueDate (BT-9) et ReasonCode (charge).
        PivotDocumentDto full = ContractFixtures.BuildFullyPopulatedDocument();
        string json = CanonicalJson.Serialize(full);

        // 1. Réflexion : CHAQUE propriété publique de CHAQUE DTO pivot doit être une clé JSON. Un champ
        //    ajouté à un DTO (et écrit par le writer) mais non exercé ici fait échouer cette garde — on est
        //    alors forcé de le peupler, donc de vérifier (étape 2) que le lecteur prod le consomme.
        using JsonDocument parsed = JsonDocument.Parse(json);
        JsonElement root = parsed.RootElement;
        AssertAllPublicPropertiesAreJsonKeys(root, typeof(PivotDocumentDto));
        JsonElement supplier = root.GetProperty("Supplier");
        AssertAllPublicPropertiesAreJsonKeys(supplier, typeof(PivotPartyDto));
        AssertAllPublicPropertiesAreJsonKeys(supplier.GetProperty("Address"), typeof(PivotAddressDto));
        AssertAllPublicPropertiesAreJsonKeys(root.GetProperty("Totals"), typeof(PivotTotalsDto));
        JsonElement line = root.GetProperty("Lines")[0];
        AssertAllPublicPropertiesAreJsonKeys(line, typeof(PivotLineDto));
        AssertAllPublicPropertiesAreJsonKeys(line.GetProperty("Taxes")[0], typeof(PivotLineTaxDto));
        AssertAllPublicPropertiesAreJsonKeys(root.GetProperty("CreditNoteRefs")[0], typeof(PivotDocumentRefDto));
        AssertAllPublicPropertiesAreJsonKeys(root.GetProperty("Payments")[0], typeof(PivotPaymentDto));
        AssertAllPublicPropertiesAreJsonKeys(root.GetProperty("DocumentCharges")[0], typeof(PivotDocumentChargeDto));

        // 1bis. Aucune propriété de TYPE VALEUR du document de référence n'est laissée à son défaut —
        //       sinon un champ valeur optionnel ajouté plus tard (ex. bool=false) passerait la garde
        //       alors que le lecteur prod le droppe (false==false au round-trip).
        AssertValueTypePropertiesAreNonDefault(full, typeof(PivotDocumentDto));
        AssertValueTypePropertiesAreNonDefault(full.Supplier!, typeof(PivotPartyDto));
        AssertValueTypePropertiesAreNonDefault(full.Supplier!.Address!, typeof(PivotAddressDto));
        AssertValueTypePropertiesAreNonDefault(full.Totals, typeof(PivotTotalsDto));
        AssertValueTypePropertiesAreNonDefault(full.Lines[0], typeof(PivotLineDto));
        AssertValueTypePropertiesAreNonDefault(full.Lines[0].Taxes[0], typeof(PivotLineTaxDto));
        AssertValueTypePropertiesAreNonDefault(full.CreditNoteRefs[0], typeof(PivotDocumentRefDto));
        AssertValueTypePropertiesAreNonDefault(full.Payments[0], typeof(PivotPaymentDto));
        AssertValueTypePropertiesAreNonDefault(full.DocumentCharges[0], typeof(PivotDocumentChargeDto));

        // 2. Le lecteur de PRODUCTION doit consommer chaque propriété présente : un champ oublié serait
        //    laissé à sa valeur par défaut dans le DTO reconstruit et la re-sérialisation divergerait.
        //    L'identité octet-par-octet du round-trip ⇒ consommation complète (INV-PIPELINE-001/002, ADR-0007).
        PivotDocumentDto rebuilt = PivotCanonicalJsonReader.Read(json);
        CanonicalJson.Serialize(rebuilt).Should().Be(
            json, "Serialize(Read(json)) == json : le lecteur prod consomme chaque champ du pivot entièrement peuplé (ADR-0007)");
        PayloadHasher.ComputeHash(rebuilt).Should().Be(
            PayloadHasher.ComputeHash(full),
            "l'empreinte survit au round-trip du lecteur prod sur un document entièrement peuplé");
    }

    private static void AssertAllPublicPropertiesAreJsonKeys(JsonElement node, Type dtoType)
    {
        foreach (PropertyInfo property in dtoType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            node.TryGetProperty(property.Name, out _).Should().BeTrue(
                "la propriété {0}.{1} doit être une clé de SON objet JSON canonique : le document de "
                + "référence doit l'exercer pour que la garde du lecteur prod la couvre (RDL02)",
                dtoType.Name,
                property.Name);
        }
    }

    // RDL02 (renfort revue) : la présence de clé + le round-trip ne suffisent PAS pour un champ
    // optionnel de TYPE VALEUR ajouté plus tard (ex. `bool isCorrected = false`) — le writer émettrait
    // toujours la clé et `false == false` rendrait le round-trip identique même si le lecteur prod droppe
    // le champ. On exige donc que chaque propriété de type valeur du document de référence porte une
    // valeur NON par défaut : tout champ valeur ajouté DOIT être peuplé ici à une valeur distinguable du
    // défaut, ce qui force le round-trip à révéler un drop du lecteur.
    private static void AssertValueTypePropertiesAreNonDefault(object instance, Type dtoType)
    {
        foreach (PropertyInfo property in dtoType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            Type valueType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (!valueType.IsValueType)
            {
                // Les types référence (string, DTO imbriqué, collection) sont couverts par la présence de
                // clé JSON (optionnel null → clé omise → garde rouge) + le round-trip.
                continue;
            }

            object? value = property.GetValue(instance);
            value.Should().NotBeNull(
                "{0}.{1} (type valeur) doit porter une valeur dans le document de référence (RDL02)",
                dtoType.Name,
                property.Name);
            value.Should().NotBe(
                Activator.CreateInstance(valueType),
                "{0}.{1} doit être ≠ de son défaut pour que le round-trip révèle un drop du lecteur prod (RDL02)",
                dtoType.Name,
                property.Name);
        }
    }

    private static string FixturesDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures", "contrat-v1");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Répertoire des golden « tests/fixtures/contrat-v1 » introuvable en remontant depuis " + AppContext.BaseDirectory);
    }
}
