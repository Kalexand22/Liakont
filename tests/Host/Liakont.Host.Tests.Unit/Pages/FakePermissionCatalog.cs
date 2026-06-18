namespace Liakont.Host.Tests.Unit.Pages;

using System.Collections.Generic;
using Stratum.Common.Abstractions.Security;

/// <summary>Stub d'<see cref="IPermissionCatalog"/> sans entrée (tests bUnit d'AdminRoleForm, RB6 P2).</summary>
internal sealed class FakePermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionCatalogEntry> GetAll() => [];
}
