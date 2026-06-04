namespace Liakont.Modules.Documents.Tests.Integration;

using Liakont.Modules.Documents.Tests.Integration.Fixtures;
using Xunit;

[CollectionDefinition("DocumentsIntegration")]
public sealed class DocumentsCollectionFixture : ICollectionFixture<DocumentsDatabaseFixture>
{
}
