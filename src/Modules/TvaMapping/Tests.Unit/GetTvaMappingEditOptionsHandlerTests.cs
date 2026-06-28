namespace Liakont.Modules.TvaMapping.Tests.Unit;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.TvaMapping.Contracts.Queries;
using Liakont.Modules.TvaMapping.Domain.Entities;
using Liakont.Modules.TvaMapping.Domain.Services;
using Liakont.Modules.TvaMapping.Infrastructure.Handlers.Queries;
using Xunit;

/// <summary>
/// Handler des listes FERMÉES d'édition (item TVA05 / WEB07b) : les CODES exposés sont EXACTEMENT ceux
/// des sources du moteur d'édition (énumérations du domaine + <see cref="VatCategoryParser.AllowedCodes"/>
/// + <see cref="VatexCatalog"/>) — impossible de faire diverger l'UI du moteur ; chaque option porte un
/// libellé non vide. Aucune valeur fiscale inventée (CLAUDE.md n°2).
/// </summary>
public sealed class GetTvaMappingEditOptionsHandlerTests
{
    private static Task<Contracts.DTOs.TvaMappingEditOptionsDto> HandleAsync() =>
        new GetTvaMappingEditOptionsHandler().Handle(new GetTvaMappingEditOptionsQuery(), CancellationToken.None);

    [Fact]
    public async Task Categories_match_the_domain_allowed_codes()
    {
        var options = await HandleAsync();

        options.Categories.Select(c => c.Code).Should().Equal(VatCategoryParser.AllowedCodes);
        options.Categories.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.Label));
    }

    [Fact]
    public async Task Parts_expose_the_MappingPart_enum_names_except_the_dead_Adjudication_part()
    {
        var options = await HandleAsync();

        // BUG-12 : la part Adjudication n'est consultée par AUCUN consommateur (CHECK lit Autre, B4 lit Frais)
        // → elle n'est jamais PROPOSÉE à la création (une règle posée dessus serait morte). Les autres codes
        // restent EXACTEMENT ceux de l'énum (impossible de faire diverger l'UI du moteur).
        var expected = Enum.GetNames<MappingPart>().Where(name => name != nameof(MappingPart.Adjudication));

        options.Parts.Select(p => p.Code).Should().Equal(expected);
        options.Parts.Select(p => p.Code).Should().NotContain(nameof(MappingPart.Adjudication));
        options.Parts.Should().OnlyContain(p => !string.IsNullOrWhiteSpace(p.Label));
    }

    [Fact]
    public async Task The_Autre_part_is_labelled_for_both_auction_and_plain_invoices()
    {
        var options = await HandleAsync();

        // BUG-12 : la part Autre est celle lue par le CHECK pour TOUTES les lignes (adjudication, factures
        // clients, notes) → un libellé qui couvre aussi les cas hors enchères (facture client simple).
        var autre = options.Parts.Single(p => p.Code == nameof(MappingPart.Autre));
        autre.Label.Should().Contain("Adjudication et factures");
    }

    [Fact]
    public async Task RateModes_match_the_RateMode_enum_names()
    {
        var options = await HandleAsync();

        options.RateModes.Select(r => r.Code).Should().Equal(Enum.GetNames<RateMode>());
        options.RateModes.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.Label));
    }

    [Fact]
    public async Task Vatex_codes_match_the_catalog()
    {
        var options = await HandleAsync();

        options.VatexCodes.Select(v => v.Code).Should().Equal(VatexCatalog.AllowedCodes);
        options.VatexCodes.Should().OnlyContain(v => !string.IsNullOrWhiteSpace(v.Label));
    }
}
