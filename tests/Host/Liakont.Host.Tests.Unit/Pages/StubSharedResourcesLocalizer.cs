namespace Liakont.Host.Tests.Unit.Pages;

using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Localization;
using Stratum.Common.UI;

/// <summary>
/// Doubles de test partagés pour les pages console bâties sur <c>DeclaredListPage</c> (SUP02 : vue d'ensemble
/// et détail de supervision) : localisation, contexte acteur et services de préférences/filtres en no-op.
/// Évite de redupliquer les mêmes stubs dans chaque page de test de supervision.
/// </summary>
internal sealed class StubSharedResourcesLocalizer : IStringLocalizer<SharedResources>
{
    public LocalizedString this[string name] => new(name, name, resourceNotFound: true);

    public LocalizedString this[string name, params object[] arguments] =>
        new(name, string.Format(CultureInfo.InvariantCulture, name, arguments), resourceNotFound: true);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}
