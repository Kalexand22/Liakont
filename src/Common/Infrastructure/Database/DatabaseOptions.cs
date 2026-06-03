namespace Stratum.Common.Infrastructure.Database;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public required string ConnectionString { get; init; }
}
