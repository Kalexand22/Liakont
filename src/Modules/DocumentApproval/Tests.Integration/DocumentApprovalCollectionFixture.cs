namespace Liakont.Modules.DocumentApproval.Tests.Integration;

using Liakont.Modules.DocumentApproval.Tests.Integration.Fixtures;
using Xunit;

/// <summary>Partage un unique conteneur PostgreSQL (mono-tenant) entre les tests de la collection.</summary>
[CollectionDefinition("DocumentApprovalIntegration")]
public sealed class DocumentApprovalCollectionFixture : ICollectionFixture<DocumentApprovalDatabaseFixture>
{
}
