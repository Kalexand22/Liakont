namespace Liakont.Host.Tests.Unit.Navigation;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Navigation;
using Liakont.Host.Security;
using Stratum.Common.Abstractions.Security;
using Xunit;

public sealed class LiakontNavSectionProviderTests
{
    [Fact]
    public void GetSection_Should_Expose_The_Liakont_Section_With_Core_Items()
    {
        var section = BuildProvider().GetSection();

        section.Title.Should().Be("Liakont");
        Labels(section).Should().Contain(["Documents", "Encaissements", "Traitements", "Paramétrage"]);
    }

    [Fact]
    public void GetSection_Should_Hide_Reconciliation_When_No_Pdf_Pool()
    {
        var section = BuildProvider(reconciliationAvailable: false).GetSection();

        Labels(section).Should().NotContain("Réconciliation");
    }

    [Fact]
    public void GetSection_Should_Show_Reconciliation_When_Pdf_Pool_Present()
    {
        var section = BuildProvider(reconciliationAvailable: true).GetSection();

        Labels(section).Should().Contain("Réconciliation");
    }

    [Fact]
    public void GetSection_Should_Embed_The_Pending_Count_In_The_Reconciliation_Label()
    {
        var section = BuildProvider(
            reconciliationAvailable: true,
            reconciliationPendingCount: 4,
            permissions: [LiakontPermissions.Actions]).GetSection();

        // Le compteur d'éléments en attente est embarqué dans le libellé (NavItem n'a pas de champ badge).
        Labels(section).Should().Contain("Réconciliation (4)");
        Labels(section).Should().NotContain("Réconciliation");
    }

    [Fact]
    public void GetSection_Should_Hide_The_Count_For_Non_Operators_Even_When_Pending()
    {
        var section = BuildProvider(reconciliationAvailable: true, reconciliationPendingCount: 4, permissions: []).GetSection();

        Labels(section).Should().Contain("Réconciliation");
        Labels(section).Should().NotContain("Réconciliation (4)");
    }

    [Fact]
    public void GetSection_Should_Omit_The_Count_When_Nothing_Pending()
    {
        var section = BuildProvider(reconciliationAvailable: true, reconciliationPendingCount: 0).GetSection();

        Labels(section).Should().Contain("Réconciliation");
    }

    [Fact]
    public void GetSection_Should_Hide_Supervision_Without_Permission()
    {
        var section = BuildProvider(permissions: []).GetSection();

        Labels(section).Should().NotContain("Supervision");
    }

    [Fact]
    public void GetSection_Should_Show_Supervision_With_Permission()
    {
        var section = BuildProvider(permissions: [LiakontPermissions.Supervision]).GetSection();

        Labels(section).Should().Contain("Supervision");
    }

    private static LiakontNavSectionProvider BuildProvider(
        bool reconciliationAvailable = false,
        int reconciliationPendingCount = 0,
        string[]? permissions = null) =>
        new(new FakePermissionService(permissions ?? []), new FakeConsoleContext(reconciliationAvailable, reconciliationPendingCount));

    private static IEnumerable<string> Labels(Stratum.Common.UI.Models.NavSection section) =>
        section.Items.Select(i => i.Label);

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

    private sealed class FakeConsoleContext : ILiakontConsoleContext
    {
        public FakeConsoleContext(bool reconciliationAvailable, int reconciliationPendingCount)
        {
            ReconciliationAvailable = reconciliationAvailable;
            ReconciliationPendingCount = reconciliationPendingCount;
        }

        public bool ReconciliationAvailable { get; }

        public int ReconciliationPendingCount { get; }

        public Task EnsureInitializedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
