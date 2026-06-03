namespace Stratum.Modules.Notification.Web.Registries;

using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;
using Stratum.Modules.Notification.Contracts.DTOs;

internal sealed class EmailTemplateColumnRegistry : ColumnRegistryBase<EmailTemplateDto>
{
    protected override void Configure()
    {
        Column("Code", "Code", "EmailTemplate", ColumnDataType.Text, defaultVisible: true, sortOrder: 0);
        Column("SubjectTemplate", "Objet", "EmailTemplate", ColumnDataType.Text, defaultVisible: true, sortOrder: 1);
        Column("Category", "Catégorie", "EmailTemplate", ColumnDataType.Text, defaultVisible: true, sortOrder: 2);
        Column("LanguageCode", "Langue", "EmailTemplate", ColumnDataType.Text, defaultVisible: true, sortOrder: 3);
        Column("EntityType", "Type entité", "EmailTemplate", ColumnDataType.Text, defaultVisible: true, sortOrder: 4);
        Column("UpdatedAt", "Dernière modif.", "EmailTemplate", ColumnDataType.Date, defaultVisible: true, sortOrder: 5);
        Column("CreatedAt", "Créé le", "EmailTemplate", ColumnDataType.Date, defaultVisible: false, sortOrder: 6);
    }
}
