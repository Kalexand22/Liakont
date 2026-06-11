namespace Liakont.Host.Tests.Unit.Navigation;

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Liakont.Host.Navigation;
using Stratum.Common.Abstractions.Security;
using Stratum.Modules.Job.Contracts;
using Xunit;

/// <summary>
/// Tests du filtre de visibilité Liakont pour la section de navigation « Jobs » du socle (FIX07c).
/// La page socle /admin/jobs exige <see cref="JobPermissions.View"/>, jamais accordée par un rôle
/// Liakont (matrice §3) : la section ne doit donc apparaître qu'au porteur effectif de cette permission
/// (super-admin). Anti-faux-vert : l'entrée est réellement présente AVEC la permission et réellement
/// absente sans elle (sinon elle mènerait à une page vide — le bug corrigé).
/// </summary>
public sealed class JobNavVisibilityFilterTests
{
    [Fact]
    public void GetSection_Hides_The_Jobs_Entry_Without_Job_View_Permission()
    {
        var section = new JobNavVisibilityFilter(new FakePermissionService([])).GetSection();

        // Section vidée → omise par BuildNavTree (sections à 0 item) : plus d'entrée morte vers une page vide.
        section.Items.Should().BeEmpty();
    }

    [Fact]
    public void GetSection_Shows_The_Jobs_Entry_With_Job_View_Permission()
    {
        var section = new JobNavVisibilityFilter(new FakePermissionService([JobPermissions.View])).GetSection();

        section.Title.Should().Be("Jobs");
        section.Items.Select(i => i.Label).Should().Contain("Planifications");
        section.Items.Select(i => i.Href).Should().Contain("/admin/jobs");
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
