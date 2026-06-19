namespace Liakont.Modules.Mandats.Tests.Unit;

using System;
using FluentAssertions;
using Liakont.Modules.Mandats.Domain.Entities;
using Xunit;

/// <summary>
/// Domaine pur de la numérotation BT-1 par mandant (MND05, ADR-0025 §5 / INV-BT1-4). Vérifie le démarrage à 1,
/// le rendu formaté (préfixe tenant + valeur, sans zéro de remplissage inventé), l'avancement « continu », le
/// type <c>bigint</c> (jamais float) et les rejets de valeurs structurellement absentes.
/// </summary>
public sealed class MandatSequenceTests
{
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid Mandant = Guid.NewGuid();

    [Fact]
    public void Start_Begins_At_One_With_Declared_Prefix()
    {
        var sequence = MandatSequence.Start(Company, Mandant, "ARM-2026-");

        sequence.CompanyId.Should().Be(Company);
        sequence.MandantId.Should().Be(Mandant);
        sequence.Prefix.Should().Be("ARM-2026-");
        sequence.NextValue.Should().Be(1L);
        sequence.UpdatedAt.Should().BeNull("aucun numéro n'a encore été alloué");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Start_Rejects_Missing_Prefix(string? prefix)
    {
        var act = () => MandatSequence.Start(Company, Mandant, prefix!);

        act.Should().Throw<ArgumentException>("le préfixe est du paramétrage tenant obligatoire, jamais un défaut inventé (CLAUDE.md n°2).");
    }

    [Fact]
    public void Start_Rejects_Empty_Tenant_Or_Mandant()
    {
        ((Action)(() => MandatSequence.Start(Guid.Empty, Mandant, "P-"))).Should().Throw<ArgumentException>();
        ((Action)(() => MandatSequence.Start(Company, Guid.Empty, "P-"))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Allocate_Formats_Prefix_Plus_Value_And_Advances_Continuously()
    {
        var sequence = MandatSequence.Start(Company, Mandant, "ARM-2026-");

        var first = sequence.Allocate();
        first.Value.Should().Be(1L);
        first.FormattedNumber.Should().Be("ARM-2026-1");
        sequence.NextValue.Should().Be(2L, "la séquence avance d'exactement 1 (continuité §1.4)");
        sequence.UpdatedAt.Should().NotBeNull();

        var second = sequence.Allocate();
        second.Value.Should().Be(2L);
        second.FormattedNumber.Should().Be("ARM-2026-2");
        sequence.NextValue.Should().Be(3L);
    }

    [Fact]
    public void Format_Uses_Invariant_Culture_Without_Padding_Or_Separators()
    {
        var sequence = MandatSequence.Start(Company, Mandant, "X-");

        sequence.Format(1L).Should().Be("X-1");
        sequence.Format(42L).Should().Be("X-42");
        sequence.Format(1234567L).Should().Be("X-1234567", "aucun séparateur de milliers ni zéro de remplissage inventé");
    }

    [Fact]
    public void NextValue_Is_Bigint_Beyond_Int32_Range()
    {
        // bigint (long), jamais float ni int32 : une séquence reconstituée au-delà d'Int32.MaxValue alloue sans débordement.
        var sequence = MandatSequence.Reconstitute(
            Company, Mandant, "ARM-", (long)int.MaxValue + 5, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var allocation = sequence.Allocate();

        allocation.Value.Should().Be((long)int.MaxValue + 5);
        allocation.FormattedNumber.Should().Be("ARM-2147483652");
        sequence.NextValue.Should().Be((long)int.MaxValue + 6);
    }
}
