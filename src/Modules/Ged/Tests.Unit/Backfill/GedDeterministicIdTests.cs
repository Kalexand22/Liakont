namespace Liakont.Modules.Ged.Tests.Unit.Backfill;

using System;
using FluentAssertions;
using Liakont.Modules.Ged.Infrastructure.Backfill;
using Xunit;

/// <summary>
/// L'identité GED d'un document backfillé est DÉTERMINISTE et STABLE d'une entrée de coffre (GED10) : c'est ce qui
/// rend le re-passage idempotent (RL-21) sans colonne d'unicité inventée. Deux entrées distinctes rendent deux
/// identités distinctes (pas de collision).
/// </summary>
public sealed class GedDeterministicIdTests
{
    [Fact]
    public void Same_archive_entry_always_maps_to_the_same_identity()
    {
        var archiveEntryId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var first = GedDeterministicId.ForArchiveEntry(archiveEntryId);
        var second = GedDeterministicId.ForArchiveEntry(archiveEntryId);

        first.Should().Be(second, "un re-passage du backfill sur la même entrée doit viser la même identité GED (idempotence)");
        first.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Different_archive_entries_map_to_different_identities()
    {
        var a = GedDeterministicId.ForArchiveEntry(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var b = GedDeterministicId.ForArchiveEntry(Guid.Parse("22222222-2222-2222-2222-222222222222"));

        a.Should().NotBe(b, "deux entrées de coffre distinctes ne doivent pas entrer en collision dans l'index GED");
    }
}
