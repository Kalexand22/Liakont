namespace Liakont.Agent.Adapters.EncheresV6.Tests;

using FluentAssertions;
using Liakont.Agent.Adapters.EncheresV6;
using Liakont.Agent.Adapters.EncheresV6.Source;
using Liakont.Agent.Adapters.EncheresV6.Tests.Fakes;
using Xunit;

/// <summary>
/// Vérifie que l'extracteur ODBC EncheresV6 (<see cref="PervasiveExtractor"/>) s'instancie et expose son
/// identité de source. (L'ancien squelette <c>EncheresV6Extractor</c> a été remplacé par le vrai
/// extracteur BA/BV — voir <see cref="PervasiveExtractorTests"/>.)
/// </summary>
public class EncheresV6ExtractorTests
{
    [Fact]
    public void SourceName_is_EncheresV6()
    {
        var extractor = new PervasiveExtractor(new RecordingConnection(), new EncheresV6Schema("enc"), "2", new RecordingAgentLog());

        extractor.SourceName.Should().Be("EncheresV6");
    }
}
