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
using Stratum.Common.UI.Models;
using Xunit;

public sealed class LiakontNavNodeProviderTests
{
    [Fact]
    public void GetNavNode_Should_Expose_The_Liakont_Tree_With_Core_Items()
    {
        var root = BuildProvider().GetNavNode();

        root.Label.Should().Be("Liakont");
        Labels(root).Should().Contain(["Documents", "Encaissements", "Traitements", "Paramétrage"]);
    }

    [Fact]
    public void GetNavNode_Should_Hide_Reconciliation_When_No_Pdf_Pool()
    {
        var root = BuildProvider(reconciliationAvailable: false).GetNavNode();

        Labels(root).Should().NotContain("Réconciliation");
    }

    [Fact]
    public void GetNavNode_Should_Show_Reconciliation_When_Pdf_Pool_Present()
    {
        var root = BuildProvider(reconciliationAvailable: true).GetNavNode();

        Labels(root).Should().Contain("Réconciliation");
    }

    [Fact]
    public void GetNavNode_Should_Embed_The_Pending_Count_In_The_Reconciliation_Label()
    {
        var root = BuildProvider(
            reconciliationAvailable: true,
            reconciliationPendingCount: 4,
            permissions: [LiakontPermissions.Actions]).GetNavNode();

        // Le compteur d'éléments en attente est embarqué dans le libellé (NavNode n'a pas de champ badge).
        Labels(root).Should().Contain("Réconciliation (4)");
        Labels(root).Should().NotContain("Réconciliation");
    }

    [Fact]
    public void GetNavNode_Should_Hide_The_Count_For_Non_Operators_Even_When_Pending()
    {
        var root = BuildProvider(reconciliationAvailable: true, reconciliationPendingCount: 4, permissions: []).GetNavNode();

        Labels(root).Should().Contain("Réconciliation");
        Labels(root).Should().NotContain("Réconciliation (4)");
    }

    [Fact]
    public void GetNavNode_Should_Omit_The_Count_When_Nothing_Pending()
    {
        var root = BuildProvider(reconciliationAvailable: true, reconciliationPendingCount: 0).GetNavNode();

        Labels(root).Should().Contain("Réconciliation");
    }

    [Fact]
    public void GetNavNode_Should_Render_Parametrage_As_A_Sub_Menu_For_Settings_Holders()
    {
        var root = BuildProvider(permissions: [LiakontPermissions.Settings]).GetNavNode();

        // Sous-menu Paramétrage (lot polish UX/UI) : une entrée par élément à paramétrer, plus la
        // vue d'ensemble. Le hub /parametrage reste la cible de la première entrée.
        var parametrage = Node(root, "Paramétrage");
        parametrage.HasChildren.Should().BeTrue("avec liakont.settings, Paramétrage est un sous-menu");
        parametrage.Children.Select(c => c.Label).Should().Equal(
            "Vue d'ensemble", "Paramètres fiscaux", "Table TVA", "Comptes PA", "Alertes & supervision", "Agents d'extraction");
        parametrage.Children.Select(c => c.Href).Should().Equal(
            "/parametrage", "/parametrage/fiscal", "/parametrage/table-tva", "/parametrage/comptes-pa", "/parametrage/alertes", "/agents");
    }

    [Fact]
    public void GetNavNode_Should_Mark_The_Overview_Entry_ExactMatch()
    {
        var root = BuildProvider(permissions: [LiakontPermissions.Settings]).GetNavNode();

        // Sans ExactMatch, /parametrage/fiscal activerait AUSSI la vue d'ensemble (préfixe).
        var overview = Node(root, "Paramétrage").Children.Single(c => c.Label == "Vue d'ensemble");
        overview.ExactMatch.Should().BeTrue();
    }

    [Fact]
    public void GetNavNode_Should_Collapse_Parametrage_To_A_Leaf_Without_Settings_Permission()
    {
        var root = BuildProvider(permissions: []).GetNavNode();

        // Sans liakont.settings : pas de sous-menu (les pages cibles refuseraient l'accès), mais le
        // hub /parametrage reste consultable en lecture.
        var parametrage = Node(root, "Paramétrage");
        parametrage.HasChildren.Should().BeFalse();
        parametrage.Href.Should().Be("/parametrage");
    }

    [Fact]
    public void GetNavNode_Should_Label_The_Extraction_Agents_Entry_Distinctly_From_Stratum_Agents()
    {
        var root = BuildProvider(permissions: [LiakontPermissions.Settings]).GetNavNode();

        // La nav Stratum (Annuaire) a déjà une entrée « Agents » (/admin/agents) : l'entrée Liakont
        // doit porter un libellé distinct pour ne pas confondre les deux concepts (bug-inbox console-web).
        var subLabels = Node(root, "Paramétrage").Children.Select(c => c.Label).ToList();
        subLabels.Should().Contain("Agents d'extraction");
        subLabels.Should().NotContain("Agents");
    }

    [Fact]
    public void GetNavNode_Should_Hide_Extraction_Agents_Without_Settings_Permission()
    {
        var root = BuildProvider(permissions: []).GetNavNode();

        AllLabels(root).Should().NotContain("Agents d'extraction");
    }

    [Fact]
    public void GetNavNode_Should_Hide_Supervision_Without_Permission()
    {
        var root = BuildProvider(permissions: []).GetNavNode();

        Labels(root).Should().NotContain("Supervision");
    }

    [Fact]
    public void GetNavNode_Should_Show_Supervision_With_Permission()
    {
        var root = BuildProvider(permissions: [LiakontPermissions.Supervision]).GetNavNode();

        Labels(root).Should().Contain("Supervision");
    }

    [Fact]
    public void GetNavNode_Should_Hide_Flotte_Without_Fleet_Permission()
    {
        // La méta-supervision de flotte (OPS04) ne s'ouvre pas avec la seule permission de supervision tenant.
        var root = BuildProvider(permissions: [LiakontPermissions.Supervision]).GetNavNode();

        Labels(root).Should().NotContain("Flotte");
    }

    [Fact]
    public void GetNavNode_Should_Show_Flotte_With_Fleet_Permission()
    {
        var root = BuildProvider(permissions: [LiakontPermissions.Fleet]).GetNavNode();

        Labels(root).Should().Contain("Flotte");
    }

    private static LiakontNavNodeProvider BuildProvider(
        bool reconciliationAvailable = false,
        int reconciliationPendingCount = 0,
        string[]? permissions = null) =>
        new(new FakePermissionService(permissions ?? []), new FakeConsoleContext(reconciliationAvailable, reconciliationPendingCount));

    private static IEnumerable<string> Labels(NavNode root) =>
        root.Children.Select(i => i.Label);

    private static IEnumerable<string> AllLabels(NavNode root) =>
        root.Children.SelectMany(c => new[] { c.Label }.Concat(c.Children.Select(g => g.Label)));

    private static NavNode Node(NavNode root, string label) =>
        root.Children.Single(c => c.Label == label);

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
