namespace Liakont.Host.Notifications;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Disponibilité EFFECTIVE de l'envoi d'email de l'instance (BUG-31) : vrai si une configuration
/// exploitable existe — config d'instance en base (chiffrée, autoritaire — ADR-0039) OU repli
/// <c>appsettings</c> (ADR-0018). Consommée par les gardes AMONT dont le comportement dépend de la
/// possibilité d'un envoi (invitation à la création d'utilisateur, réinitialisation de mot de passe :
/// sans envoi possible, le mot de passe temporaire est remis UNE fois à l'opérateur). Ne jamais tester
/// <see cref="SmtpOptions.IsConfigured"/> pour cela : il ignore la config d'instance en base.
/// </summary>
internal interface IEmailSendAvailability
{
    /// <summary>Vrai si le transport email de l'instance peut réellement envoyer.</summary>
    Task<bool> IsConfiguredAsync(CancellationToken ct = default);
}
