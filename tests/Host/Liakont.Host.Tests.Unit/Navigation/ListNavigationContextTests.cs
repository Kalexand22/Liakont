namespace Liakont.Host.Tests.Unit.Navigation;

using FluentAssertions;
using Stratum.Common.UI.Navigation;
using Xunit;

// BUG-19 — résolution des voisins (préc/suiv) dans l'ordre AFFICHÉ capturé d'une liste. Logique pure (aucune
// dépendance Blazor) : bornes, hors-liste, normalisation d'URL, remplacement de contexte (transversalité).
public sealed class ListNavigationContextTests
{
    [Fact]
    public void Resolve_returns_none_when_nothing_captured()
    {
        new ListNavigationContext().Resolve("documents/abc").HasContext.Should().BeFalse();
    }

    [Fact]
    public void Resolve_returns_none_when_the_current_url_is_not_in_the_captured_list()
    {
        var ctx = new ListNavigationContext();
        ctx.Capture(["/documents/a", "/documents/b"]);

        ctx.Resolve("documents/zzz").HasContext.Should().BeFalse("une fiche ouverte hors de la grille n'a pas de voisins");
    }

    [Fact]
    public void Resolve_gives_previous_and_next_in_the_middle_of_the_list()
    {
        var ctx = new ListNavigationContext();
        ctx.Capture(["/documents/a", "/documents/b", "/documents/c"]);

        var neighbors = ctx.Resolve("documents/b");

        neighbors.HasContext.Should().BeTrue();
        neighbors.Previous.Should().Be("/documents/a");
        neighbors.Next.Should().Be("/documents/c");
        neighbors.Index.Should().Be(1);
        neighbors.Total.Should().Be(3);
    }

    [Fact]
    public void Resolve_has_no_previous_at_the_first_record()
    {
        var ctx = new ListNavigationContext();
        ctx.Capture(["/documents/a", "/documents/b"]);

        var neighbors = ctx.Resolve("documents/a");

        neighbors.Previous.Should().BeNull("le premier enregistrement n'a pas de précédent");
        neighbors.Next.Should().Be("/documents/b");
        neighbors.Index.Should().Be(0);
    }

    [Fact]
    public void Resolve_has_no_next_at_the_last_record()
    {
        var ctx = new ListNavigationContext();
        ctx.Capture(["/documents/a", "/documents/b"]);

        var neighbors = ctx.Resolve("documents/b");

        neighbors.Next.Should().BeNull("le dernier enregistrement n'a pas de suivant");
        neighbors.Previous.Should().Be("/documents/a");
        neighbors.Index.Should().Be(1);
    }

    [Fact]
    public void Resolve_matches_ignoring_leading_slash_query_and_case()
    {
        var ctx = new ListNavigationContext();
        ctx.Capture(["/Documents/ABC", "/Documents/DEF"]);

        // URL courante relative (sans slash de tête), casse différente, query parasite.
        var neighbors = ctx.Resolve("documents/abc?tab=2");

        neighbors.HasContext.Should().BeTrue("la comparaison normalise slash de bordure, casse et query/fragment");
        neighbors.Next.Should().Be("/Documents/DEF", "on navigue vers l'URL capturée telle quelle");
    }

    [Fact]
    public void Capture_replaces_the_previous_context_transversally()
    {
        var ctx = new ListNavigationContext();
        ctx.Capture(["/documents/a", "/documents/b"]);

        // L'opérateur a parcouru une AUTRE liste (transverse) : la nouvelle capture remplace l'ancienne.
        ctx.Capture(["/emissions-marge-b2c/x", "/emissions-marge-b2c/y"]);

        ctx.Resolve("documents/a").HasContext.Should().BeFalse("l'ancien contexte de liste est remplacé");
        ctx.Resolve("emissions-marge-b2c/x").Next.Should().Be("/emissions-marge-b2c/y");
    }
}
