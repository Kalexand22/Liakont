namespace Liakont.Modules.Archive.Tests.Integration.Fixtures;

using Xunit;

/// <summary>Collection xUnit : un conteneur PostgreSQL dédié à la preuve de sauvegarde/restauration (OPS01b).</summary>
[CollectionDefinition("BackupRestoreIntegration")]
public sealed class BackupRestoreCollectionFixture : ICollectionFixture<BackupRestoreDatabaseFixture>;
