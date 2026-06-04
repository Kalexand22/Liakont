namespace Liakont.Modules.Reconciliation.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.Reconciliation.Domain;
using Xunit;

public sealed class DocumentNumberMatcherTests
{
    [Fact]
    public void Contains_DelimitedNumber_IsFound()
    {
        DocumentNumberMatcher.Contains("scan facture FAC-2026-0042.pdf", "FAC-2026-0042").Should().BeTrue();
    }

    [Fact]
    public void Contains_NumberAsPrefixOfLongerToken_IsNotFound()
    {
        // « FAC-2026-0042 » ne doit pas matcher à l'intérieur de « FAC-2026-00421 » (INV-RECONCILIATION-001).
        DocumentNumberMatcher.Contains("FAC-2026-00421", "FAC-2026-0042").Should().BeFalse();
    }

    [Fact]
    public void Contains_IsCaseInsensitive()
    {
        DocumentNumberMatcher.Contains("fichier fac-2026-0042 recu", "FAC-2026-0042").Should().BeTrue();
    }

    [Theory]
    [InlineData(null, "FAC-1")]
    [InlineData("", "FAC-1")]
    [InlineData("texte", "")]
    [InlineData("texte", "   ")]
    public void Contains_EmptyInputs_AreFalse(string? haystack, string needle)
    {
        DocumentNumberMatcher.Contains(haystack, needle).Should().BeFalse();
    }
}
