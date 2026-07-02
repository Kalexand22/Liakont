namespace Liakont.Host.Tests.Unit.InstanceEmail;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Stratum.Modules.Notification.Contracts;

/// <summary>Faux transport email : mémorise les envois (destinataire/sujet/corps) et peut simuler un échec.</summary>
internal sealed class RecordingEmailTransport : IEmailTransport
{
    public List<(string Recipient, string Subject, string Body)> Sent { get; } = [];

    public bool ThrowOnSend { get; set; }

    public Task SendAsync(string recipient, string subject, string body, CancellationToken ct = default)
    {
        if (ThrowOnSend)
        {
            throw new InvalidOperationException("Échec d'envoi simulé.");
        }

        Sent.Add((recipient, subject, body));
        return Task.CompletedTask;
    }
}
