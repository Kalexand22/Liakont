namespace Liakont.Agent.Cli.Tests.Hosting;

using System.Net;
using System.Net.Http;
using FluentAssertions;
using Liakont.Agent.Cli.Hosting;
using Xunit;

/// <summary>
/// RDF01 : preuve que le client HTTP du CHEMIN DE RUN RÉEL (AgentRunComposition, partagé par le
/// service Windows et la commande CLI `run`) force TLS 1.2/1.3 — et pas seulement la sonde test-api,
/// qui tourne dans un processus distinct (ServicePointManager est un statique global au processus).
/// Avant RDF01, CreateHttpClient ne posait jamais le protocole → faux-vert (CLAUDE.md règle review n°8).
/// </summary>
public sealed class AgentRunCompositionTlsTests
{
    [Fact]
    public void CreateHttpClient_force_TLS_1_2_et_1_3_sur_le_chemin_de_run()
    {
        using HttpClient client = AgentRunComposition.CreateHttpClient("https://plateforme.example.test/");

        client.BaseAddress!.AbsoluteUri.Should().Be("https://plateforme.example.test/");
        (ServicePointManager.SecurityProtocol & SecurityProtocolType.Tls12).Should().Be(SecurityProtocolType.Tls12);
        (ServicePointManager.SecurityProtocol & SecurityProtocolType.Tls13).Should().Be(SecurityProtocolType.Tls13);
    }
}
