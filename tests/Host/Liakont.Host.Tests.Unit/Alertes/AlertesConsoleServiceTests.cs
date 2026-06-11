namespace Liakont.Host.Tests.Unit.Alertes;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Alertes;
using Liakont.Modules.Supervision.Contracts;
using Liakont.Modules.Supervision.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using MediatR;
using Xunit;

/// <summary>
/// <see cref="AlertesConsoleService"/>.<c>SaveRoutingAsync</c> (FIX212) : décode les lignes (cible + CSV),
/// ignore les lignes vides, REJETTE une clé de règle inconnue (entrée morte silencieuse), et émet la commande
/// de remplacement de matrice. La garde de clé s'appuie sur le catalogue exposé par le Contract Supervision.
/// </summary>
public sealed class AlertesConsoleServiceTests
{
    [Fact]
    public async Task Decodes_Rows_And_Sends_The_Replace_Command()
    {
        var sender = new FakeSender();
        var service = new AlertesConsoleService(sender, new FakeDeviceQueries());

        var form = new AlertesRoutingFormModel
        {
            Rows =
            {
                new AlertesRoutingRow { Selector = "rule:documents.pa_rejected", RecipientsCsv = "compta@acme.test" },
                new AlertesRoutingRow { Selector = "severity:Critical", RecipientsCsv = "it@acme.test; admin@acme.test" },
            },
        };

        await service.SaveRoutingAsync(form);

        var command = sender.Sent.OfType<SetAlertRoutingMatrixCommand>().Should().ContainSingle().Subject;
        command.Rules.Should().HaveCount(2);
        command.Rules[0].RuleKey.Should().Be("documents.pa_rejected");
        command.Rules[0].Recipients.Should().Equal("compta@acme.test");
        command.Rules[1].Severity.Should().Be("Critical");
        command.Rules[1].Recipients.Should().Equal("it@acme.test", "admin@acme.test");
    }

    [Fact]
    public async Task Skips_Entirely_Empty_Rows()
    {
        var sender = new FakeSender();
        var service = new AlertesConsoleService(sender, new FakeDeviceQueries());

        var form = new AlertesRoutingFormModel
        {
            Rows =
            {
                new AlertesRoutingRow { Selector = string.Empty, RecipientsCsv = "   " },
                new AlertesRoutingRow { Selector = "rule:agent.mute", RecipientsCsv = "it@acme.test" },
            },
        };

        await service.SaveRoutingAsync(form);

        var command = sender.Sent.OfType<SetAlertRoutingMatrixCommand>().Should().ContainSingle().Subject;
        command.Rules.Should().ContainSingle();
        command.Rules[0].RuleKey.Should().Be("agent.mute");
    }

    [Fact]
    public async Task Rejects_An_Unknown_Rule_Key()
    {
        var sender = new FakeSender();
        var service = new AlertesConsoleService(sender, new FakeDeviceQueries());

        var form = new AlertesRoutingFormModel
        {
            Rows = { new AlertesRoutingRow { Selector = "rule:regle.inexistante", RecipientsCsv = "it@acme.test" } },
        };

        var act = () => service.SaveRoutingAsync(form);

        await act.Should().ThrowAsync<ArgumentException>();
        sender.Sent.Should().BeEmpty();
    }

    private sealed class FakeDeviceQueries : IAlertDeviceQueries
    {
        public Task<AlertDeviceStatusDto> GetDeviceStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AlertDeviceStatusDto
            {
                OperatorEmailConfigured = true,
                EvaluationIntervalMinutes = 15,
                Rules = new List<AlertRuleStatusDto>
                {
                    new() { RuleKey = "agent.mute", DisplayName = "Agent muet", Severity = "Critique", IsActive = true, ThresholdDisplay = "> 24 h" },
                    new() { RuleKey = "documents.pa_rejected", DisplayName = "Rejets PA", Severity = "Critique", IsActive = true, ThresholdDisplay = "> 2 j" },
                },
            });
    }

    private sealed class FakeSender : ISender
    {
        public List<object> Sent { get; } = [];

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            Sent.Add(request!);
            return Task.CompletedTask;
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Sent.Add(request);
            return Task.FromResult(default(TResponse)!);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
