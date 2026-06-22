namespace Liakont.PaClients.ChorusPro.Tests.Unit;

using FluentAssertions;
using Xunit;

/// <summary>
/// Garde de sécurité C5 (CP03) : Chorus Pro est le 1er connecteur HTTP réellement en prod, et base64 n'est
/// PAS du chiffrement — les credentials (secret PISTE, mot de passe du compte technique, en-tête
/// <c>cpro-account</c>) ne doivent JAMAIS fuir dans une représentation diagnostique ou un message
/// d'exception (CLAUDE.md n°10). Le plug-in n'injecte aucun <see cref="System.IDisposable"/> de log : la
/// seule surface diagnostique est <see cref="object.ToString"/> et les messages d'exception, vérifiés ici.
/// </summary>
public sealed class ChorusProCredentialSafetyTests
{
    [Fact]
    public void Account_Config_ToString_Redacts_The_Piste_Secret_And_The_Technical_Password()
    {
        var rendered = StubChorusProAccountResolver.Config.ToString();

        rendered.Should().NotContain("secret-FICTIF", "le client_secret PISTE est caviardé (CLAUDE.md n°10)");
        rendered.Should().NotContain("mdp-FICTIF", "le mot de passe du compte technique est caviardé (CLAUDE.md n°10)");

        // L'identité non sensible (environnement, compte) reste lisible pour le diagnostic opérateur.
        rendered.Should().Contain("ACC-FICTIF");
        rendered.Should().Contain("***");
    }

    [Fact]
    public void Account_Config_ToString_Does_Not_Leak_The_Piste_Client_Id()
    {
        // Le client_id n'est pas un secret au même titre que le secret, mais il identifie le compte PISTE :
        // la représentation caviardée ne l'expose pas non plus (transport mémoire, jamais journalisé).
        StubChorusProAccountResolver.Config.ToString().Should().NotContain("client-FICTIF");
    }
}
