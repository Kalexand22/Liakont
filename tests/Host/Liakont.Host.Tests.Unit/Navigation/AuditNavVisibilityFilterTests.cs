namespace Liakont.Host.Tests.Unit.Navigation;

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Liakont.Host.Navigation;
using Liakont.Host.Security;
using Stratum.Common.Abstractions.Security;
using Stratum.Modules.Audit.Contracts;
using Xunit;

/// <summary>
/// Tests du filtre de visibilité Liakont pour la section de navigation « Audit » du socle (FIX303).
/// Les pages socle /admin/audit et /admin/audit/policies exigent <see cref="AuditPermissions.AuditView"/>,
/// jamais accordée par un rôle Liakont (matrice §3) : la section ne doit donc apparaître qu'au porteur effectif
/// de cette permission (super-admin). Anti-faux-vert : la section est réellement présente AVEC la permission
/// (« Journal d'audit » seul — « Politiques » est retiré de la nav, décision recette 2026-07-01) et réellement
/// absente sans elle (sinon elle mènerait à des pages vides — le bug corrigé).
/// </summary>
public sealed class AuditNavVisibilityFilterTests
{
    [Fact]
    public void GetSection_Hides_The_Audit_Section_Without_Audit_View_Permission()
    {
        var section = new AuditNavVisibilityFilter(new FakePermissionService([])).GetSection();

        // Section vidée → omise par BuildNavTree (sections à 0 item) : plus d'entrée morte vers des pages vides.
        section.Items.Should().BeEmpty();
    }

    [Fact]
    public void GetSection_Keeps_A_Liakont_Role_Out_Of_Audit_Even_With_All_Liakont_Permissions()
    {
        // Un rôle Liakont au plus haut niveau (superviseur) porte read/actions/settings/supervision mais JAMAIS
        // audit.trail.view (matrice §3 immuable) : la section Audit reste masquée — surface super-admin uniquement.
        var allLiakontPermissions = new[]
        {
            LiakontPermissions.Read,
            LiakontPermissions.Actions,
            LiakontPermissions.Settings,
            LiakontPermissions.Supervision,
        };

        var section = new AuditNavVisibilityFilter(new FakePermissionService(allLiakontPermissions)).GetSection();

        section.Items.Should().BeEmpty();
    }

    [Fact]
    public void GetSection_Shows_Only_The_Journal_Entry_With_Audit_View_Permission()
    {
        var section = new AuditNavVisibilityFilter(new FakePermissionService([AuditPermissions.AuditView])).GetSection();

        section.Title.Should().Be("Audit");

        // « Journal d'audit » reste accessible au super-admin.
        section.Items.Select(i => i.Label).Should().Contain("Journal d'audit");
        section.Items.Select(i => i.Href).Should().Contain("/admin/audit");

        // « Politiques » (/admin/audit/policies) est RETIRÉE de la nav (décision Karl, recette 2026-07-01) :
        // écran socle de configuration de l'audit GÉNÉRIQUE, sans valeur produit — masqué de la sidebar même
        // pour un super-admin (la ROUTE reste ouverte). Anti-faux-vert : on prouve l'absence, section non vide.
        section.Items.Should().NotBeEmpty();
        section.Items.Select(i => i.Label).Should().NotContain("Politiques");
        section.Items.Select(i => i.Href).Should().NotContain("/admin/audit/policies");
    }

    private sealed class FakePermissionService : IPermissionService
    {
        private readonly HashSet<string> _permissions;

        public FakePermissionService(string[] permissions) =>
            _permissions = new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);

        public event Action? OnPermissionsChanged
        {
            add { }
            remove { }
        }

        public bool HasPermission(string permission) => _permissions.Contains(permission);
    }
}
