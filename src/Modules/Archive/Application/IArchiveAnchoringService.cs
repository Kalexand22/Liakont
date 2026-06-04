namespace Liakont.Modules.Archive.Application;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Ancre la tête de chaîne du coffre du tenant COURANT (TRK06). Appelé par <c>DailyAnchoringTenantJob</c>
/// (un tenant par invocation, via <c>TenantJobRunner</c>, SOL06) et à la demande. Idempotent : une tête
/// déjà ancrée par la méthode configurée n'est pas réancrée. La preuve produite est archivée dans le
/// coffre (write-once) et indexée dans <c>documents.archive_anchors</c>.
/// </summary>
public interface IArchiveAnchoringService
{
    /// <summary>Ancre la tête de chaîne actuelle du tenant courant (no-op idempotent si déjà ancrée).</summary>
    Task<AnchoringOutcome> AnchorChainHeadAsync(CancellationToken cancellationToken = default);
}
