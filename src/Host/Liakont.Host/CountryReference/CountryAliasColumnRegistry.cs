namespace Liakont.Host.CountryReference;

using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;

/// <summary>
/// Colonnes de la liste du référentiel de correspondance pays (ADR-0038, Lot 4), pilotant <c>DeclaredListPage</c>
/// (tri, recherche, choix / réordonnancement des colonnes, export) — aucune grille « maison ». Le formatage de la
/// date de modification (fuseau navigateur) est porté par le ColumnTemplate de la page ; le tri / la recherche
/// reposent sur les propriétés de <see cref="CountryAliasRow"/> nommées ici.
/// </summary>
internal sealed class CountryAliasColumnRegistry : ColumnRegistryBase<CountryAliasRow>
{
    protected override void Configure()
    {
        Column("SourceCode", "Code source", "CountryAlias", ColumnDataType.Text, defaultVisible: true, sortOrder: 0);
        Column("IsoCode", "Code ISO", "CountryAlias", ColumnDataType.Text, defaultVisible: true, sortOrder: 1);
        Column("UpdatedAtUtc", "Modifié le", "CountryAlias", ColumnDataType.Date, defaultVisible: true, sortOrder: 2);
    }
}
