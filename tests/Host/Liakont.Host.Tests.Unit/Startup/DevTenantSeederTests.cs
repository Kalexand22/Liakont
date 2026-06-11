namespace Liakont.Host.Tests.Unit.Startup;

using FluentAssertions;
using Liakont.Host.Startup;
using Xunit;

/// <summary>
/// Décision d'amorçage idempotente PAR COMPOSANT du seed de dev (FIX203a). Couvre les trois branches :
/// boot à froid → import complet ; profil présent + fichier de mapping disponible → rattrapage de la
/// table seule (récupération d'un seed partiel — le cœur du bug corrigé) ; profil présent sans fichier
/// de mapping → rien à amorcer.
/// </summary>
public sealed class DevTenantSeederTests
{
    [Fact]
    public void Cold_Boot_Without_Profile_Imports_The_Full_Seed()
    {
        // Aucun profil : import complet quel que soit l'état du fichier de mapping (il est inclus dans le seed).
        DevTenantSeeder.DecideSeedAction(profileExists: false, mappingSeedFileExists: false)
            .Should().Be(DevTenantSeeder.DevSeedAction.ImportFullSeed);
        DevTenantSeeder.DecideSeedAction(profileExists: false, mappingSeedFileExists: true)
            .Should().Be(DevTenantSeeder.DevSeedAction.ImportFullSeed);
    }

    [Fact]
    public void Existing_Profile_With_Missing_Mapping_Table_Triggers_Backfill_Not_Full_Reimport()
    {
        // Récupération d'un seed partiel (le bug FIX203a) : profil committé mais table absente au re-boot.
        // L'action doit RATTRAPER la table — jamais ré-importer le paramétrage (qui écraserait des
        // réglages édités via la console).
        DevTenantSeeder.DecideSeedAction(profileExists: true, mappingSeedFileExists: true)
            .Should().Be(DevTenantSeeder.DevSeedAction.BackfillMappingTable);
    }

    [Fact]
    public void Existing_Profile_Without_A_Mapping_Seed_File_Does_Nothing()
    {
        DevTenantSeeder.DecideSeedAction(profileExists: true, mappingSeedFileExists: false)
            .Should().Be(DevTenantSeeder.DevSeedAction.Skip);
    }
}
