namespace Liakont.Modules.Archive.Tests.Integration.Fixtures;

using Xunit;

/// <summary>Collection xUnit partageant un unique conteneur PostgreSQL entre les tests d'intégration Archive.</summary>
[CollectionDefinition("ArchiveIntegration")]
public sealed class ArchiveCollectionFixture : ICollectionFixture<ArchiveDatabaseFixture>;
