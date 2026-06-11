namespace Liakont.Modules.Archive.Tests.Unit.Doubles;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Archive.Contracts;

/// <summary>
/// Double d'<see cref="IFiscalControlExportService"/> pour isoler le service de réversibilité (API03) de
/// l'assemblage interne de l'export contrôle fiscal. Retourne un dossier d'archive canné.
/// </summary>
internal sealed class FakeFiscalControlExportService : IFiscalControlExportService
{
    public DateOnly? LastRangeFrom { get; private set; }

    public DateOnly? LastRangeTo { get; private set; }

    public bool RangeCalled { get; private set; }

    public Task<FiscalControlExport> BuildForDocumentAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<FiscalControlExport> BuildForPeriodAsync(int year, int? month, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<FiscalControlExport> BuildForRangeAsync(DateOnly? fromInclusive, DateOnly? toInclusive, CancellationToken cancellationToken = default)
    {
        RecordRange(fromInclusive, toInclusive);

        var verification = new ArchiveVerificationReport(
            new ArchiveIntegrityReport(true, 1, [], null),
            [],
            false,
            true,
            "Coffre intègre (test).");

        return Task.FromResult(new FiscalControlExport("plage:—..—", CannedFiles(), verification, true, "notice"));
    }

    public IAsyncEnumerable<FiscalExportFile> StreamForDocumentAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public async IAsyncEnumerable<FiscalExportFile> StreamForRangeAsync(DateOnly? fromInclusive, DateOnly? toInclusive, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        RecordRange(fromInclusive, toInclusive);
        await Task.CompletedTask;
        foreach (FiscalExportFile file in CannedFiles())
        {
            yield return file;
        }
    }

    private static List<FiscalExportFile> CannedFiles() =>
    [
        new FiscalExportFile("2026/05/F-2026-001/manifest.json", "application/json", Encoding.UTF8.GetBytes("{\"files\":[]}")),
        new FiscalExportFile("rapport-integrite.json", "application/json", Encoding.UTF8.GetBytes("{}")),
        new FiscalExportFile("notice-verification.txt", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("notice")),
    ];

    private void RecordRange(DateOnly? fromInclusive, DateOnly? toInclusive)
    {
        RangeCalled = true;
        LastRangeFrom = fromInclusive;
        LastRangeTo = toInclusive;
    }
}
