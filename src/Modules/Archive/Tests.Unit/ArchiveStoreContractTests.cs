namespace Liakont.Modules.Archive.Tests.Unit;

using System;
using System.Linq;
using FluentAssertions;
using Liakont.Modules.Archive.Domain;
using Xunit;

/// <summary>
/// Garde structurelle du WORM (CLAUDE.md n°4) : l'abstraction du coffre n'expose AUCUNE méthode de
/// modification ou de suppression d'un objet existant — l'immuabilité est dans la forme de l'interface,
/// pas seulement dans le comportement. Un futur ajout d'un <c>Delete</c>/<c>Update</c> ferait échouer ce test.
/// </summary>
public sealed class ArchiveStoreContractTests
{
    private static readonly string[] ForbiddenVerbs = ["delete", "remove", "update", "overwrite", "purge", "clear", "replace", "truncate"];

    [Fact]
    public void IArchiveStore_ExposesNoMutationOrDeletionMethod()
    {
        string[] methodNames = typeof(IArchiveStore).GetMethods().Select(m => m.Name.ToLowerInvariant()).ToArray();

        methodNames.Should().NotContain(name => ForbiddenVerbs.Any(name.Contains));
    }

    [Fact]
    public void IArchiveStore_OnlyExposesWriteExistsRead()
    {
        string[] methodNames = typeof(IArchiveStore).GetMethods()
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .ToArray();

        methodNames.Should().BeEquivalentTo("WriteAsync", "ExistsAsync", "ReadAsync");
    }
}
