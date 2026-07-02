namespace Liakont.Host.Tests.Unit.Navigation;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Navigation;
using Liakont.Host.Security;
using Microsoft.AspNetCore.Http;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.UI.Models;
using Xunit;

public sealed class LiakontNavNodeProviderTests
{
    [Fact]
    public void GetNavNode_Should_Expose_The_Liakont_Tree_With_Core_Items()
    {
        // Un porteur des trois permissions éditeur (≈ rôle superviseur) voit les 4 entrées cœur.
        var root = BuildProvider(permissions:
            [LiakontPermissions.Read, LiakontPermissions.Actions, LiakontPermissions.Settings]).GetNavNode();

        root.Label.Should().Be("Liakont");
        Labels(root).Should().Contain(["Documents", "Encaissements", "Traitements", "Paramétrage"]);
    }

    [Fact]
    public void GetNavNode_Should_Hide_Documents_And_Encaissements_Without_Read_Permission()
    {
        // Documents / Encaissements (consultation) sont gardés par liakont.read (finding F5a / RLF03) :
        // un principal sans cette permission (ex. exploitant de flotte) ne les voit pas.
        var root = BuildProvider(permissions: []).GetNavNode();

        Labels(root).Should().NotContain("Documents");
        Labels(root).Should().NotContain("Encaissements");
    }

    [Fact]
    public void GetNavNode_Should_Show_Documents_And_Encaissements_With_Read_Permission()
    {
        var root = BuildProvider(permissions: [LiakontPermissions.Read]).GetNavNode();

        Labels(root).Should().Contain("Documents").And.Contain("Encaissements");
    }

    [Fact]
    public void GetNavNode_Should_Hide_Traitements_Without_Read_Permission()
    {
        // Le journal des traitements est une surface de CONSULTATION (liakont.read — matrice §3 « journaux »,
        // guide opérateur §17, endpoint GET /runs) : un principal sans read (ex. exploitant de flotte) ne le voit pas.
        var root = BuildProvider(permissions: []).GetNavNode();

        Labels(root).Should().NotContain("Traitements");
    }

    [Fact]
    public void GetNavNode_Should_Show_Traitements_With_Read_Permission()
    {
        // Un lecteur (liakont.read) consulte le journal des traitements (lecture seule — seul POST /runs/trigger
        // exige liakont.actions).
        var root = BuildProvider(permissions: [LiakontPermissions.Read]).GetNavNode();

        Labels(root).Should().Contain("Traitements");
    }

    [Fact]
    public void GetNavNode_Should_Show_Signatures_With_Read_Permission()
    {
        // Signatures (SIG10) : surface de CONSULTATION du workflow de validation/signature, gardée par liakont.read
        // (mêmes conditions que Documents/Encaissements/Traitements ; les actions exigent liakont.actions sur la page).
        var root = BuildProvider(permissions: [LiakontPermissions.Read]).GetNavNode();

        Labels(root).Should().Contain("Signatures");
    }

    [Fact]
    public void GetNavNode_Should_Hide_Signatures_Without_Read_Permission()
    {
        var root = BuildProvider(permissions: []).GetNavNode();

        Labels(root).Should().NotContain("Signatures");
    }

    [Fact]
    public void GetNavNode_Should_Show_Tva_Declaration_With_Read_Permission()
    {
        // TVA / Déclaration (L2) : surface de CONSULTATION (aide à la déclaration de TVA sous le régime de la
        // marge), gardée par liakont.read comme les autres surfaces de consultation ; la page /tva-declaration
        // porte la même policy.
        var root = BuildProvider(permissions: [LiakontPermissions.Read]).GetNavNode();

        Labels(root).Should().Contain("TVA / Déclaration");
    }

    [Fact]
    public void GetNavNode_Should_Hide_Tva_Declaration_Without_Read_Permission()
    {
        var root = BuildProvider(permissions: []).GetNavNode();

        Labels(root).Should().NotContain("TVA / Déclaration");
    }

    [Fact]
    public void GetNavNode_Should_Show_Ged_Search_With_GedRead_Permission()
    {
        // GED (GED09a) : surface de consultation gardée par la permission DÉDIÉE liakont.ged.read (silo GED, F19 §6.5).
        var root = BuildProvider(permissions: [LiakontPermissions.GedRead]).GetNavNode();

        Labels(root).Should().Contain("Recherche GED");
    }

    [Fact]
    public void GetNavNode_Should_Hide_Ged_Search_Without_GedRead_Permission()
    {
        // La permission de lecture FISCALE (liakont.read) N'OUVRE PAS la GED : un lecteur sans liakont.ged.read
        // ne voit pas « Recherche GED » (silo GED — la fuite ne se déplace pas de la GED vers le fiscal).
        var root = BuildProvider(permissions: [LiakontPermissions.Read]).GetNavNode();

        Labels(root).Should().NotContain("Recherche GED");
    }

    [Fact]
    public void GetNavNode_For_A_Reader_Should_Show_All_Consultation_Entries_And_The_Settings_Hub_As_A_Leaf()
    {
        // Preuve d'acceptance RLF03 (rôle `lecture`, matrice §3 : liakont.read seul) : il consulte
        // Documents/Encaissements/Traitements (toutes des surfaces read) et garde l'accès au HUB Paramétrage
        // (export d'audit par période, FIX208 — capacité liakont.read ; la masquer régresserait cette capacité
        // d'audit). Le hub est un simple lien — le SOUS-MENU de paramétrage reste réservé à liakont.settings.
        var root = BuildProvider(permissions: [LiakontPermissions.Read]).GetNavNode();

        Labels(root).Should().Contain(["Documents", "Encaissements", "Traitements"]);

        var parametrage = Node(root, "Paramétrage");
        parametrage.HasChildren.Should().BeFalse("sans liakont.settings, Paramétrage est un simple lien vers le hub");
        parametrage.Href.Should().Be("/parametrage");
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
            "Vue d'ensemble", "Profil légal", "Paramètres fiscaux", "Table TVA", "Référentiel pays", "Comptes PA", "Alertes & supervision", "Agents d'extraction");
        parametrage.Children.Select(c => c.Href).Should().Equal(
            "/parametrage", "/parametrage/profil", "/parametrage/fiscal", "/parametrage/table-tva", "/parametrage/referentiel-pays", "/parametrage/comptes-pa", "/parametrage/alertes", "/agents");
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
    public void GetNavNode_Should_Hide_Parametrage_Without_Read_Permission()
    {
        var root = BuildProvider(permissions: []).GetNavNode();

        // Sans liakont.read NI liakont.settings (ex. exploitant de flotte), le hub Paramétrage est masqué :
        // le trou « page de paramétrage ouvrable par tout authentifié » (finding F5a / RLF03) est fermé.
        Labels(root).Should().NotContain("Paramétrage");
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
        // Un porteur de liakont.read sans liakont.settings obtient le hub en simple lien — donc aucune
        // entrée « Agents d'extraction » (réservée au sous-menu des porteurs de liakont.settings).
        var root = BuildProvider(permissions: [LiakontPermissions.Read]).GetNavNode();

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

    [Fact]
    public void Supervision_Should_Be_A_Branch_With_Overview_And_Clients()
    {
        // OPS03 lot C : Supervision devient un sous-menu — « Vue d'ensemble » (ExactMatch :
        // /supervision/{tenantId} existe) et « Clients » (administration d'instance).
        var root = BuildProvider(permissions: [LiakontPermissions.Supervision]).GetNavNode();

        var supervision = root.Children.Single(n => n.Label == "Supervision");
        supervision.Children.Should().HaveCount(2);

        var overview = supervision.Children[0];
        overview.Label.Should().Be("Vue d'ensemble");
        overview.Href.Should().Be("/supervision");
        overview.ExactMatch.Should().BeTrue();

        var clients = supervision.Children[1];
        clients.Label.Should().Be("Clients");
        clients.Href.Should().Be("/clients");
    }

    [Fact]
    public void Supervision_Should_Hide_Email_Config_Without_Instance_Settings_Permission()
    {
        // ADR-0039 : « Configuration email » est un geste d'ÉCRITURE d'instance (liakont.instance.settings),
        // distinct de la lecture seule liakont.supervision. Un superviseur SANS instance.settings ne la voit pas.
        var root = BuildProvider(permissions: [LiakontPermissions.Supervision]).GetNavNode();

        var supervision = root.Children.Single(n => n.Label == "Supervision");
        supervision.Children.Select(c => c.Label).Should().NotContain("Configuration email");
    }

    [Fact]
    public void Supervision_Should_Show_Email_Config_With_Instance_Settings_Permission()
    {
        // Porteur de liakont.supervision (aire d'instance visible) ET de liakont.instance.settings (écriture) :
        // l'entrée « Configuration email » apparaît, rangée dans l'aire opérateur d'instance (ADR-0039, /email-instance).
        var root = BuildProvider(permissions:
            [LiakontPermissions.Supervision, LiakontPermissions.InstanceSettings]).GetNavNode();

        var supervision = root.Children.Single(n => n.Label == "Supervision");
        var email = supervision.Children.Single(c => c.Label == "Configuration email");
        email.Href.Should().Be("/email-instance");
    }

    [Fact]
    public void GetNavNode_For_A_CrossTenant_SuperAdmin_Should_Hide_All_Tenant_Scoped_Entries()
    {
        // RB1 : un super-admin (stratum-admin) opère en cross-tenant ; même avec toutes les permissions et
        // un pool de réconciliation présent, les surfaces TENANT-SCOPÉES ne doivent PAS apparaître.
        var root = BuildProvider(
            reconciliationAvailable: true,
            reconciliationPendingCount: 3,
            permissions: [LiakontPermissions.Read, LiakontPermissions.Actions, LiakontPermissions.Settings],
            isCrossTenantAdmin: true).GetNavNode();

        AllLabels(root).Should().NotContain([
            "Documents", "Encaissements", "Traitements", "Signatures", "Réconciliation",
            "Réconciliation (3)", "Paramétrage",
        ]);
    }

    [Fact]
    public void GetNavNode_For_A_CrossTenant_SuperAdmin_Should_Keep_CrossTenant_Entries()
    {
        // Les surfaces CROSS-TENANT (Supervision, Clients, Flotte) restent visibles pour le super-admin.
        var root = BuildProvider(
            permissions: [LiakontPermissions.Supervision, LiakontPermissions.Fleet],
            isCrossTenantAdmin: true).GetNavNode();

        Labels(root).Should().Contain(["Supervision", "Flotte"]);
        AllLabels(root).Should().Contain("Clients");
    }

    [Fact]
    public void GetNavNode_Should_Treat_A_SuperAdmin_From_HttpContext_As_CrossTenant_During_Prerender()
    {
        // RB1 — au PRÉRENDU SSR (pas de circuit → console non initialisée, IsCrossTenantAdmin=false), le
        // super-admin est détecté via le HttpContext de la requête → les surfaces tenant-scopées sont
        // masquées DÈS le prérendu (pas de « flash »), et la nav reste correcte même sans circuit.
        var superAdmin = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.Role, "stratum-admin")], authenticationType: "test"));
        var root = BuildProvider(
            permissions: [LiakontPermissions.Read, LiakontPermissions.Settings],
            isCrossTenantAdmin: false,
            httpUser: superAdmin).GetNavNode();

        AllLabels(root).Should().NotContain(["Documents", "Encaissements", "Traitements", "Signatures", "Paramétrage"]);
    }

    private static LiakontNavNodeProvider BuildProvider(
        bool reconciliationAvailable = false,
        int reconciliationPendingCount = 0,
        string[]? permissions = null,
        bool isCrossTenantAdmin = false,
        ClaimsPrincipal? httpUser = null) =>
        new(
            new FakePermissionService(permissions ?? []),
            new FakeConsoleContext(reconciliationAvailable, reconciliationPendingCount, isCrossTenantAdmin),
            new StubHttpContextAccessor(httpUser));

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
        public FakeConsoleContext(bool reconciliationAvailable, int reconciliationPendingCount, bool isCrossTenantAdmin = false)
        {
            ReconciliationAvailable = reconciliationAvailable;
            ReconciliationPendingCount = reconciliationPendingCount;
            IsCrossTenantAdmin = isCrossTenantAdmin;
        }

        public bool ReconciliationAvailable { get; }

        public int ReconciliationPendingCount { get; }

        public bool IsCrossTenantAdmin { get; }

        public Task EnsureInitializedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubHttpContextAccessor : IHttpContextAccessor
    {
        public StubHttpContextAccessor(ClaimsPrincipal? user)
        {
            if (user is not null)
            {
                HttpContext = new DefaultHttpContext { User = user };
            }
        }

        public HttpContext? HttpContext { get; set; }
    }
}
