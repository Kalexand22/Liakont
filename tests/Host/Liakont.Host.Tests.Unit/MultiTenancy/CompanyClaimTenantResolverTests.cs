namespace Liakont.Host.Tests.Unit.MultiTenancy;

using System;
using System.Security.Claims;
using FluentAssertions;
using Liakont.Host.MultiTenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Stratum.Common.Abstractions.MultiTenancy;
using Xunit;

/// <summary>
/// Tests unitaires du résolveur autoritaire <see cref="CompanyClaimTenantResolver"/> (ADR-0021 §2c) :
/// dérive le tenant du claim <c>company_id</c> du jeton, via <see cref="ICompanyTenantLookup"/> (caché,
/// fail-soft).
/// </summary>
public sealed class CompanyClaimTenantResolverTests
{
    private static readonly Guid KnownCompanyId = Guid.Parse("00000000-0000-4000-a000-000000000001");

    [Fact]
    public void Resolve_Returns_Tenant_When_CompanyId_Claim_Resolves()
    {
        var lookup = new RecordingLookup(KnownCompanyId, "default");
        var resolver = BuildResolver(lookup, claim: KnownCompanyId.ToString());

        resolver.Resolve().Should().Be("default");
        lookup.LastQueried.Should().Be(KnownCompanyId);
    }

    [Fact]
    public void Resolve_Returns_Null_When_CompanyId_Claim_Has_No_Matching_Tenant()
    {
        // Le rejet sur contradiction est RLM03 ; ici un claim non résolu retombe simplement sur null.
        var lookup = new RecordingLookup(KnownCompanyId, "default");
        var resolver = BuildResolver(lookup, claim: Guid.Parse("99999999-9999-4999-a999-999999999999").ToString());

        resolver.Resolve().Should().BeNull();
    }

    [Fact]
    public void Resolve_Returns_Null_And_Skips_Lookup_When_No_CompanyId_Claim()
    {
        var lookup = new RecordingLookup(KnownCompanyId, "default");
        var resolver = BuildResolver(lookup, claim: null);

        resolver.Resolve().Should().BeNull();
        lookup.CallCount.Should().Be(0, "sans claim company_id (non authentifié / chemin agent) on ne touche pas la base");
    }

    [Fact]
    public void Resolve_Returns_Null_And_Skips_Lookup_When_CompanyId_Claim_Is_Malformed()
    {
        var lookup = new RecordingLookup(KnownCompanyId, "default");
        var resolver = BuildResolver(lookup, claim: "pas-un-guid");

        resolver.Resolve().Should().BeNull();
        lookup.CallCount.Should().Be(0, "un claim non-Guid ne déclenche aucune requête");
    }

    [Fact]
    public void Resolve_Returns_Null_When_No_HttpContext()
    {
        var lookup = new RecordingLookup(KnownCompanyId, "default");
        var resolver = new CompanyClaimTenantResolver(
            new HttpContextAccessor { HttpContext = null }, lookup, NewCache(), NullLogger<CompanyClaimTenantResolver>.Instance);

        resolver.Resolve().Should().BeNull();
        lookup.CallCount.Should().Be(0);
    }

    [Fact]
    public void Resolve_Is_FailSoft_And_Returns_Null_When_Lookup_Throws()
    {
        // Une panne de lecture du registre ne doit PAS avorter la chaîne : on retombe sur null (repli).
        var lookup = new ThrowingLookup();
        var resolver = BuildResolver(lookup, claim: KnownCompanyId.ToString());

        var act = () => resolver.Resolve();

        act.Should().NotThrow();
        resolver.Resolve().Should().BeNull();
    }

    [Fact]
    public void Resolve_Caches_Positive_Result_And_Queries_Once()
    {
        var lookup = new RecordingLookup(KnownCompanyId, "default");
        var resolver = BuildResolver(lookup, claim: KnownCompanyId.ToString());

        resolver.Resolve().Should().Be("default");
        resolver.Resolve().Should().Be("default");

        lookup.CallCount.Should().Be(1, "le mapping company_id→tenant est mis en cache (un seul aller-retour base)");
    }

    private static CompanyClaimTenantResolver BuildResolver(ICompanyTenantLookup lookup, string? claim)
    {
        var ctx = new DefaultHttpContext();
        if (claim is not null)
        {
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(CompanyClaimTenantResolver.CompanyIdClaimType, claim)],
                authenticationType: "test"));
        }

        var accessor = new HttpContextAccessor { HttpContext = ctx };
        return new CompanyClaimTenantResolver(accessor, lookup, NewCache(), NullLogger<CompanyClaimTenantResolver>.Instance);
    }

    private static MemoryCache NewCache() => new(new MemoryCacheOptions());

    private sealed class RecordingLookup : ICompanyTenantLookup
    {
        private readonly Guid _known;
        private readonly string _tenantId;

        public RecordingLookup(Guid known, string tenantId)
        {
            _known = known;
            _tenantId = tenantId;
        }

        public int CallCount { get; private set; }

        public Guid? LastQueried { get; private set; }

        public string? FindTenantId(Guid companyId)
        {
            CallCount++;
            LastQueried = companyId;
            return companyId == _known ? _tenantId : null;
        }
    }

    private sealed class ThrowingLookup : ICompanyTenantLookup
    {
        public string? FindTenantId(Guid companyId) => throw new InvalidOperationException("registre indisponible");
    }
}
