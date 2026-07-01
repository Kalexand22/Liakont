namespace Liakont.Modules.Pipeline.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Pipeline.Infrastructure;
using Liakont.Modules.Reference.Contracts;
using Liakont.Modules.Reference.Contracts.DTOs;
using Xunit;

/// <summary>
/// Normalisation READ-TIME du code pays acheteur (ADR-0038) via le référentiel de correspondance. Invariants :
/// un alias connu (ENG→GB, JAP→JP) remplace le code ; un code non mappé reste BRUT (fail-closed → bloqué BT-55) ;
/// null-safe (acheteur / adresse / pays absent → document inchangé, sans même interroger le référentiel) ;
/// idempotent (un code déjà ISO n'est pas ré-écrit) ; et le Rebuild préserve TOUS les autres champs (party +
/// adresse + pivot).
/// </summary>
public class PivotCountryNormalizerTests
{
    [Fact]
    public async Task Normalizes_a_known_buyer_country_alias_to_its_iso_code()
    {
        var referential = new FakeReferential(("ENG", "GB"), ("JAP", "JP"));

        var eng = await PivotCountryNormalizer.NormalizeAsync(PivotWithBuyerCountry("ENG"), referential);
        eng.Customer!.Address!.CountryCode.Should().Be("GB");

        var jap = await PivotCountryNormalizer.NormalizeAsync(PivotWithBuyerCountry("JAP"), referential);
        jap.Customer!.Address!.CountryCode.Should().Be("JP");
    }

    [Fact]
    public async Task Leaves_an_unmapped_country_raw_and_returns_the_same_instance()
    {
        var referential = new FakeReferential(("ENG", "GB"));
        var pivot = PivotWithBuyerCountry("ZZ");

        var result = await PivotCountryNormalizer.NormalizeAsync(pivot, referential);

        result.Customer!.Address!.CountryCode.Should().Be("ZZ", "un code non mappé reste brut → bloqué en aval par BT-55");
        result.Should().BeSameAs(pivot, "aucune reconstruction quand rien ne change (identité d'objet préservée)");
    }

    [Fact]
    public async Task Is_idempotent_on_an_already_iso_code()
    {
        // « GB » n'est PAS une clé source du référentiel (les clés sont les codes legacy ENG/JAP…) → renvoyé tel
        // quel, aucune reconstruction : re-normaliser un pivot déjà normalisé est un no-op.
        var referential = new FakeReferential(("ENG", "GB"));
        var pivot = PivotWithBuyerCountry("GB");

        var result = await PivotCountryNormalizer.NormalizeAsync(pivot, referential);

        result.Should().BeSameAs(pivot);
        result.Customer!.Address!.CountryCode.Should().Be("GB");
    }

    [Fact]
    public async Task Is_null_safe_when_the_buyer_or_address_or_country_is_absent()
    {
        var referential = new FakeReferential(("ENG", "GB"));

        var noCustomer = PivotWithCustomer(customer: null);
        (await PivotCountryNormalizer.NormalizeAsync(noCustomer, referential)).Should().BeSameAs(noCustomer);

        var noAddress = PivotWithCustomer(new PivotPartyDto(name: "Acheteur"));
        (await PivotCountryNormalizer.NormalizeAsync(noAddress, referential)).Should().BeSameAs(noAddress);

        var blankCountry = PivotWithCustomer(new PivotPartyDto(name: "Acheteur", address: new PivotAddressDto(city: "Lorient", countryCode: "  ")));
        (await PivotCountryNormalizer.NormalizeAsync(blankCountry, referential)).Should().BeSameAs(blankCountry);

        referential.ResolveCalls.Should().BeEmpty("aucun code pays exploitable → le référentiel n'est même pas interrogé");
    }

    [Fact]
    public async Task Rebuild_preserves_the_other_party_address_and_pivot_fields()
    {
        var referential = new FakeReferential(("ENG", "GB"));
        var pivot = new PivotDocumentDto(
            sourceDocumentKind: "BA",
            number: "100264",
            issueDate: new DateTime(2026, 6, 1),
            sourceReference: "encheresv6:ba:100264",
            supplier: new PivotPartyDto(name: "SVV", siren: "123456782"),
            totals: new PivotTotalsDto(100m, 20m, 120m),
            operationCategory: null,
            customer: new PivotPartyDto(
                name: "Buyer Ltd",
                siren: "998877666",
                vatNumber: "GB123",
                address: new PivotAddressDto(line1: "1 High St", postalCode: "EC1", city: "London", countryCode: "ENG"),
                email: "b@x.uk",
                isCompanyHint: true),
            paymentDueDate: new DateTime(2026, 7, 1),
            isB2cReportingDeclaration: true);

        var result = await PivotCountryNormalizer.NormalizeAsync(pivot, referential);

        result.Should().NotBeSameAs(pivot, "le pays a changé → le pivot est reconstruit");
        result.Customer!.Address!.CountryCode.Should().Be("GB");

        // La party conserve tout le reste.
        result.Customer.Name.Should().Be("Buyer Ltd");
        result.Customer.Siren.Should().Be("998877666");
        result.Customer.VatNumber.Should().Be("GB123");
        result.Customer.Email.Should().Be("b@x.uk");
        result.Customer.IsCompanyHint.Should().BeTrue();
        result.Customer.Address.Line1.Should().Be("1 High St");
        result.Customer.Address.PostalCode.Should().Be("EC1");
        result.Customer.Address.City.Should().Be("London");

        // Les autres champs du pivot survivent au Rebuild.
        result.Supplier!.Siren.Should().Be("123456782");
        result.Number.Should().Be("100264");
        result.PaymentDueDate.Should().Be(new DateTime(2026, 7, 1));
        result.IsB2cReportingDeclaration.Should().BeTrue();
    }

    private static PivotDocumentDto PivotWithBuyerCountry(string countryCode) =>
        PivotWithCustomer(new PivotPartyDto(name: "Acheteur", address: new PivotAddressDto(countryCode: countryCode)));

    private static PivotDocumentDto PivotWithCustomer(PivotPartyDto? customer) =>
        new(
            sourceDocumentKind: "BA",
            number: "A-1",
            issueDate: new DateTime(2026, 6, 1),
            sourceReference: "encheresv6:ba:1",
            supplier: null,
            totals: new PivotTotalsDto(100m, 20m, 120m),
            operationCategory: null,
            customer: customer);

    private sealed class FakeReferential : ICountryAliasReferential
    {
        private readonly Dictionary<string, string> _map;

        public FakeReferential(params (string Source, string Iso)[] aliases)
        {
            _map = aliases.ToDictionary(a => a.Source, a => a.Iso, StringComparer.OrdinalIgnoreCase);
        }

        public List<string?> ResolveCalls { get; } = [];

        public Task<string?> ResolveAsync(string? rawCountryCode, CancellationToken cancellationToken = default)
        {
            ResolveCalls.Add(rawCountryCode);
            if (string.IsNullOrWhiteSpace(rawCountryCode))
            {
                return Task.FromResult<string?>(rawCountryCode);
            }

            return Task.FromResult<string?>(
                _map.TryGetValue(rawCountryCode.Trim().ToUpperInvariant(), out var iso) ? iso : rawCountryCode);
        }

        public Task<IReadOnlyList<CountryAliasDto>> GetAliasesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
