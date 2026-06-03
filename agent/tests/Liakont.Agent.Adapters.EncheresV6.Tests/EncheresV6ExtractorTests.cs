namespace Liakont.Agent.Adapters.EncheresV6.Tests;

using FluentAssertions;
using Xunit;

/// <summary>
/// Vérifie que le squelette de l'adaptateur EncheresV6 compile et expose son identité de
/// source. EncheresV6Extractor est visible sans using : Liakont.Agent.Adapters.EncheresV6
/// est un namespace englobant de ce projet de test.
/// </summary>
public class EncheresV6ExtractorTests
{
    [Fact]
    public void SourceName_is_EncheresV6()
    {
        var extractor = new EncheresV6Extractor();

        extractor.SourceName.Should().Be("EncheresV6");
    }
}
