namespace Stratum.Modules.Job.Web.Registries;

using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;
using Stratum.Modules.Job.Contracts.DTOs;

internal sealed class ScheduleColumnRegistry : ColumnRegistryBase<ScheduleDto>
{
    protected override void Configure()
    {
        Column("Name", "Nom", "Schedule", ColumnDataType.Text, defaultVisible: true, sortOrder: 0);
        Column("CronExpression", "Cron", "Schedule", ColumnDataType.Text, defaultVisible: true, sortOrder: 1);
        Column("JobType", "Type de job", "Schedule", ColumnDataType.Text, defaultVisible: true, sortOrder: 2);
        Column("IsActive", "Actif", "Schedule", ColumnDataType.Boolean, defaultVisible: true, sortOrder: 3);
        Column("NextRunAt", "Prochaine exécution", "Schedule", ColumnDataType.Date, defaultVisible: true, sortOrder: 4);
        Column("LastRunAt", "Dernière exécution", "Schedule", ColumnDataType.Date, defaultVisible: true, sortOrder: 5);
        Column("CreatedAt", "Créé le", "Schedule", ColumnDataType.Date, defaultVisible: false, sortOrder: 6);
        Column("UpdatedAt", "Modifié le", "Schedule", ColumnDataType.Date, defaultVisible: false, sortOrder: 7);
    }
}
