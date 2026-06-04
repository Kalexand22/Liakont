namespace Liakont.Modules.TvaMapping.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.TvaMapping.Infrastructure.Seed;
using Stratum.Common.Abstractions.Exceptions;
using Xunit;

/// <summary>
/// Chemins de rejet de <see cref="MappingTableSeedReader"/> : fichier absent, JSON invalide, seed
/// nulle, et ParseEnum sur des valeurs hors liste pour Part / RateMode / DefaultBehavior (CLAUDE.md
/// n°2 — aucune règle fiscale n'est devinée). Couvre également la conversion valide et la
/// tolérance casse pour les noms d'énumération.
/// </summary>
public sealed class MappingTableSeedReaderTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static MappingTableSeed ValidSeed() => new()
    {
        MappingVersion = "v1",
        DefaultBehavior = "Block",
        Rules =
        [
            new MappingRuleSeed
            {
                SourceRegimeCode = "X",
                Part = "Adjudication",
                Category = "S",
                RateMode = "Fixed",
                RateValue = 20m,
            },
        ],
    };

    private static string TempFilePath() =>
        Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");

    private static async Task<string> WriteTempAsync(string content)
    {
        var path = TempFilePath();
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    // ── ReadFileAsync — fichier absent → NotFoundException ────────────────────────────────────

    [Fact]
    public async Task ReadFileAsync_MissingFile_Throws_NotFoundException()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");

        // File intentionally NOT created.
        var act = async () => await MappingTableSeedReader.ReadFileAsync(path);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ── ReadFileAsync — JSON tronqué → ConflictException ─────────────────────────────────────

    [Fact]
    public async Task ReadFileAsync_InvalidJson_Throws_ConflictException()
    {
        var path = await WriteTempAsync("{ \"MappingVersion\": \"v1\", INVALID_JSON");
        try
        {
            var act = async () => await MappingTableSeedReader.ReadFileAsync(path);
            await act.Should().ThrowAsync<ConflictException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── ReadFileAsync — contenu JSON `null` → ConflictException ──────────────────────────────

    [Fact]
    public async Task ReadFileAsync_NullJsonLiteral_Throws_ConflictException()
    {
        var path = await WriteTempAsync("null");
        try
        {
            var act = async () => await MappingTableSeedReader.ReadFileAsync(path);
            await act.Should().ThrowAsync<ConflictException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── ToMappingTable — Part inconnu → ArgumentException ────────────────────────────────────

    [Theory]
    [InlineData("Bidon")]
    [InlineData("")]
    [InlineData("0")]
    public void ToMappingTable_UnknownPart_Throws_ArgumentException(string part)
    {
        var seed = ValidSeed() with
        {
            Rules =
            [
                new MappingRuleSeed
                {
                    SourceRegimeCode = "X",
                    Part = part,
                    Category = "S",
                    RateMode = "Fixed",
                    RateValue = 20m,
                },
            ],
        };

        var act = () => MappingTableSeedReader.ToMappingTable(seed, Guid.NewGuid());
        act.Should().Throw<ArgumentException>();
    }

    // ── ToMappingTable — RateMode inconnu → ArgumentException ────────────────────────────────

    [Theory]
    [InlineData("Bidon")]
    [InlineData("")]
    [InlineData("1")]
    public void ToMappingTable_UnknownRateMode_Throws_ArgumentException(string rateMode)
    {
        var seed = ValidSeed() with
        {
            Rules =
            [
                new MappingRuleSeed
                {
                    SourceRegimeCode = "X",
                    Part = "Adjudication",
                    Category = "S",
                    RateMode = rateMode,
                    RateValue = 20m,
                },
            ],
        };

        var act = () => MappingTableSeedReader.ToMappingTable(seed, Guid.NewGuid());
        act.Should().Throw<ArgumentException>();
    }

    // ── ToMappingTable — DefaultBehavior inconnu → ArgumentException ──────────────────────────

    [Theory]
    [InlineData("Bidon")]
    [InlineData("")]
    public void ToMappingTable_UnknownDefaultBehavior_Throws_ArgumentException(string behavior)
    {
        var seed = ValidSeed() with { DefaultBehavior = behavior };

        var act = () => MappingTableSeedReader.ToMappingTable(seed, Guid.NewGuid());
        act.Should().Throw<ArgumentException>();
    }

    // ── ToMappingTable — Category inconnue → ArgumentException ───────────────────────────────

    [Fact]
    public void ToMappingTable_UnknownCategory_Throws_ArgumentException()
    {
        var seed = ValidSeed() with
        {
            Rules =
            [
                new MappingRuleSeed
                {
                    SourceRegimeCode = "X",
                    Part = "Adjudication",
                    Category = "XX",
                    RateMode = "Fixed",
                    RateValue = 20m,
                },
            ],
        };

        var act = () => MappingTableSeedReader.ToMappingTable(seed, Guid.NewGuid());
        act.Should().Throw<ArgumentException>();
    }

    // ── ToMappingTable — seed valide — conversion réussit ────────────────────────────────────

    [Fact]
    public void ToMappingTable_ValidSeed_Succeeds()
    {
        var seed = ValidSeed();
        var act = () => MappingTableSeedReader.ToMappingTable(seed, Guid.NewGuid());
        act.Should().NotThrow();
    }

    // ── ToMappingTable — noms d'enum insensibles à la casse ──────────────────────────────────

    [Theory]
    [InlineData("adjudication", "fixed", "block")]
    [InlineData("ADJUDICATION", "FIXED", "BLOCK")]
    [InlineData("Adjudication", "Fixed", "Block")]
    public void ToMappingTable_CaseInsensitiveEnumNames_Accepted(string part, string rateMode, string behavior)
    {
        var seed = new MappingTableSeed
        {
            MappingVersion = "v1",
            DefaultBehavior = behavior,
            Rules =
            [
                new MappingRuleSeed
                {
                    SourceRegimeCode = "X",
                    Part = part,
                    Category = "S",
                    RateMode = rateMode,
                    RateValue = 20m,
                },
            ],
        };

        var act = () => MappingTableSeedReader.ToMappingTable(seed, Guid.NewGuid());
        act.Should().NotThrow();
    }

    // ── ImportFileAsync — fichier absent → NotFoundException ─────────────────────────────────

    [Fact]
    public async Task ImportFileAsync_MissingFile_Throws_NotFoundException()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");

        var act = async () => await MappingTableSeedReader.ImportFileAsync(path, Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
