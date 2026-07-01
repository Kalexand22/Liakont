namespace Liakont.Modules.Ged.Application;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Fabrique d'<see cref="IGedIndexUnitOfWork"/> : ouvre une connexion tenant-scopée + une transaction fraîche
/// (F19 §3.7). Chaque appel démarre une unité de travail indépendante — deux écritures concurrentes ouvrent deux
/// transactions distinctes, sérialisées par la garde de concurrence (RL-02).
/// </summary>
public interface IGedIndexUnitOfWorkFactory
{
    Task<IGedIndexUnitOfWork> BeginAsync(CancellationToken cancellationToken = default);
}
