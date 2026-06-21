namespace Liakont.Agent.Core.Tests.Net;

using System.Net;
using FluentAssertions;
using Liakont.Agent.Core.Net;
using Xunit;

/// <summary>
/// RDF01 : le point centralisé de durcissement TLS de l'agent net48 doit ajouter TLS 1.2 ET 1.3
/// aux protocoles du processus, sans jamais effacer un protocole déjà actif. ServicePointManager
/// étant un statique global au processus, ces tests n'assertent que la PRÉSENCE de bits (opérations
/// OU bit-à-bit, monotones) — sûres même si d'autres tests forcent TLS en parallèle.
/// </summary>
public sealed class AgentTlsTests
{
    [Fact]
    public void ForceStrongTls_ajoute_Tls12_et_Tls13_aux_protocoles_du_processus()
    {
        AgentTls.ForceStrongTls();

        (ServicePointManager.SecurityProtocol & SecurityProtocolType.Tls12).Should().Be(SecurityProtocolType.Tls12);
        (ServicePointManager.SecurityProtocol & SecurityProtocolType.Tls13).Should().Be(SecurityProtocolType.Tls13);
    }

    [Fact]
    public void ForceStrongTls_n_efface_pas_un_protocole_deja_actif_et_est_idempotent()
    {
        // OU bit-à-bit : tout protocole déjà actif doit survivre (after ⊇ before). On ne nomme aucun
        // protocole déprécié (CA5364) — on capture l'état courant et on prouve le sur-ensemble.
        SecurityProtocolType before = ServicePointManager.SecurityProtocol;

        AgentTls.ForceStrongTls();
        SecurityProtocolType afterFirst = ServicePointManager.SecurityProtocol;
        AgentTls.ForceStrongTls();
        SecurityProtocolType afterSecond = ServicePointManager.SecurityProtocol;

        (afterFirst & before).Should().Be(before, "le OU bit-à-bit n'efface aucun protocole déjà actif");
        afterSecond.Should().Be(afterFirst, "le forçage est idempotent");
        (afterSecond & SecurityProtocolType.Tls12).Should().Be(SecurityProtocolType.Tls12);
        (afterSecond & SecurityProtocolType.Tls13).Should().Be(SecurityProtocolType.Tls13);
    }
}
