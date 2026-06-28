namespace Liakont.Host.Tests.Unit.Startup;

using FluentAssertions;
using Liakont.Host.Startup;
using Xunit;

/// <summary>
/// Décision d'amorçage idempotente PAR COMPOSANT du seed de dev (FIX203a). Couvre les trois branches :
/// boot à froid (aucun paramétrage) → import complet ; paramétrage présent + fichier de mapping disponible →
/// rattrapage de la table seule (récupération d'un seed partiel — le cœur du bug corrigé) ; paramétrage
/// présent sans fichier de mapping → rien à amorcer. Depuis BUG-14 l'idempotence s'ancre sur la présence
/// d'un composant de paramétrage (et non plus sur le profil, l'identité n'étant plus seedée).
/// </summary>
public sealed class DevTenantSeederTests
{
    [Fact]
    public void Cold_Boot_Without_Any_Configuration_Imports_The_Full_Seed()
    {
        // Aucun paramétrage : import complet quel que soit l'état du fichier de mapping (inclus dans le seed).
        DevTenantSeeder.DecideSeedAction(alreadyConfigured: false, mappingSeedFileExists: false)
            .Should().Be(DevTenantSeeder.DevSeedAction.ImportFullSeed);
        DevTenantSeeder.DecideSeedAction(alreadyConfigured: false, mappingSeedFileExists: true)
            .Should().Be(DevTenantSeeder.DevSeedAction.ImportFullSeed);
    }

    [Fact]
    public void Existing_Configuration_With_Missing_Mapping_Table_Triggers_Backfill_Not_Full_Reimport()
    {
        // Récupération d'un seed partiel (le bug FIX203a) : paramétrage présent mais table absente au re-boot.
        // L'action doit RATTRAPER la table — jamais ré-importer le paramétrage (qui écraserait des
        // réglages édités via la console).
        DevTenantSeeder.DecideSeedAction(alreadyConfigured: true, mappingSeedFileExists: true)
            .Should().Be(DevTenantSeeder.DevSeedAction.BackfillMappingTable);
    }

    [Fact]
    public void Existing_Configuration_Without_A_Mapping_Seed_File_Does_Nothing()
    {
        DevTenantSeeder.DecideSeedAction(alreadyConfigured: true, mappingSeedFileExists: false)
            .Should().Be(DevTenantSeeder.DevSeedAction.Skip);
    }
}
