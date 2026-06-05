namespace Liakont.Agent.Cli.Tests.Commands;

using System;
using System.IO;
using FluentAssertions;
using Liakont.Agent.Cli;
using Liakont.Agent.Cli.Commands;
using Liakont.Agent.Cli.Diagnostics;
using Liakont.Agent.Core.Storage;
using Xunit;

/// <summary>
/// Commande <c>show-queue</c> (F12 §2.1, §2.3) : affiche l'état de la file locale. La lecture est
/// injectée (doublure). File vide → message dédié ; file peuplée → comptages et éléments listés.
/// </summary>
public class ShowQueueCommandTests
{
    [Fact]
    public void Empty_queue_prints_empty_message_and_returns_ok()
    {
        var command = new ShowQueueCommand(() => new QueueSnapshot(0, 0, 0, 0, Array.Empty<QueuedItem>()));
        using var output = new StringWriter();

        int code = command.Execute(Array.Empty<string>(), output);

        code.Should().Be(CliExitCode.Ok);
        output.ToString().Should().Contain("vide");
    }

    [Fact]
    public void Non_empty_queue_lists_counts_and_items()
    {
        var item = new QueuedItem(
            id: 7,
            kind: QueueItemKind.Document,
            sourceReference: "FAC-2026-001",
            payloadHash: "abcdef",
            payloadJson: null,
            filePath: null,
            status: QueueItemStatus.Error,
            attempts: 3,
            lastError: "délai dépassé",
            createdAtUtc: DateTime.UtcNow,
            updatedAtUtc: DateTime.UtcNow);
        var command = new ShowQueueCommand(() => new QueueSnapshot(5, 2, 1, 2, new[] { item }));
        using var output = new StringWriter();

        int code = command.Execute(Array.Empty<string>(), output);

        code.Should().Be(CliExitCode.Ok);
        string text = output.ToString();
        text.Should().Contain("5 élément").And.Contain("2 en attente").And.Contain("FAC-2026-001").And.Contain("délai dépassé");
    }

    [Fact]
    public void Reader_failure_returns_execution_error()
    {
        var command = new ShowQueueCommand(() => throw new InvalidOperationException("base verrouillée"));
        using var output = new StringWriter();

        int code = command.Execute(Array.Empty<string>(), output);

        code.Should().Be(CliExitCode.ExecutionError);
        output.ToString().Should().Contain("impossible");
    }
}
