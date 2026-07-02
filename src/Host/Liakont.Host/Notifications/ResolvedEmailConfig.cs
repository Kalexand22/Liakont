namespace Liakont.Host.Notifications;

using System.Text;
using Liakont.Host.InstanceEmail;
using Liakont.Modules.FleetSupervision.Application;

/// <summary>
/// Configuration d'envoi effective résolue par <see cref="SmtpEmailTransport"/> (ADR-0039) : la
/// <see cref="Shape"/> (hôte/port/TLS/expéditeur/utilisateur) porte la connexion et la composition du message ;
/// l'auth dépend du <see cref="Kind"/> — <see cref="Password"/> déchiffré pour SMTP basic, ou <see cref="OAuth"/>
/// (requête de jeton) pour XOAUTH2.
/// </summary>
internal sealed record ResolvedEmailConfig(
    SmtpOptions Shape,
    EmailProviderKind Kind,
    string? Password,
    EmailOAuthTokenRequest? OAuth)
{
    // ToString() synthétisé REDACTÉ : le mot de passe SMTP déchiffré (et l'éventuel OAuth) ne doivent jamais
    // fuir par un log accidentel (CLAUDE.md n°10/18). OAuth a son propre PrintMembers redacté.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Host = ").Append(Shape.Host)
            .Append(", Kind = ").Append(Kind)
            .Append(", Password = ").Append(Password is null ? "null" : "***")
            .Append(", OAuth = ").Append(OAuth is null ? "null" : "***");
        return true;
    }
}
