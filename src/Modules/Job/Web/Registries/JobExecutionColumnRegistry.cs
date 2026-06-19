// Liakont addition (FIX211/FIX210 §4.20/§4.21 catalogue et executions de jobs) - not part of the original Stratum vendoring.
namespace Stratum.Modules.Job.Web.Registries;

using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;
using Stratum.Modules.Job.Contracts.DTOs;

// Liakont addition (FIX211) : colonnes de la liste des EXÉCUTIONS de jobs (job.jobs). Le type est rendu via un
// template (libellé FR du catalogue), jamais le FullName stocké.
internal sealed class JobExecutionColumnRegistry : ColumnRegistryBase<JobDto>
{
    protected override void Configure()
    {
        Column("Type", "Type de job", "JobExecution", ColumnDataType.Text, defaultVisible: true, sortOrder: 0);
        Column("Status", "Statut", "JobExecution", ColumnDataType.Text, defaultVisible: true, sortOrder: 1);
        Column("CreatedAt", "Créé le", "JobExecution", ColumnDataType.Date, defaultVisible: true, sortOrder: 2);
        Column("StartedAt", "Démarré le", "JobExecution", ColumnDataType.Date, defaultVisible: true, sortOrder: 3);
        Column("CompletedAt", "Terminé le", "JobExecution", ColumnDataType.Date, defaultVisible: true, sortOrder: 4);
        Column("RetryCount", "Tentatives", "JobExecution", ColumnDataType.Number, defaultVisible: true, sortOrder: 5);
        Column("ErrorMessage", "Erreur", "JobExecution", ColumnDataType.Text, defaultVisible: false, sortOrder: 6);
    }
}
