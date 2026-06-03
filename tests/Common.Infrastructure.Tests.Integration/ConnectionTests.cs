namespace Stratum.Common.Infrastructure.Tests.Integration;

using FluentAssertions;
using Stratum.Common.Testing;
using Xunit;

public sealed class ConnectionTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public ConnectionTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task OpenAsyncShouldReturnOpenConnection()
    {
        var factory = _fixture.CreateConnectionFactory();

        using var connection = await factory.OpenAsync();

        connection.State.Should().Be(System.Data.ConnectionState.Open);
    }
}
