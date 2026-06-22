namespace Liakont.PaClients.ChorusPro.Tests.Unit;

using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Résolveur de compte FICTIF pour les tests : rend une <see cref="ChorusProAccountConfig"/> fixe (URLs +
/// creds PISTE / compte technique FICTIFS, jamais de vraie donnée client — CLAUDE.md n°7/15). Joue le rôle
/// que le Host tient en production (déchiffrement des secrets du coffre du tenant) sans aucun secret réel.
/// </summary>
internal sealed class StubChorusProAccountResolver : IChorusProAccountResolver
{
    /// <summary>Configuration fictive partagée par les tests (qualification, URLs sandbox PISTE de forme).</summary>
    public static readonly ChorusProAccountConfig Config = new(
        ChorusProEnvironment.Qualification,
        new Uri("https://sandbox-api.piste.gouv.fr/cpro/", UriKind.Absolute),
        new Uri("https://sandbox-oauth.piste.gouv.fr/api/oauth/token", UriKind.Absolute),
        accountId: "ACC-FICTIF",
        pisteClientId: "client-FICTIF",
        pisteClientSecret: "secret-FICTIF",
        technicalLogin: "login-FICTIF",
        technicalPassword: "mdp-FICTIF",
        connectionEmail: "tech-FICTIF@example.test");

    /// <inheritdoc />
    public ChorusProAccountConfig Resolve(PaAccountDescriptor account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return Config;
    }
}
