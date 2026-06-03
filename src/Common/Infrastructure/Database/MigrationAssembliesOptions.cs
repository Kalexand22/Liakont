namespace Stratum.Common.Infrastructure.Database;

using System.Reflection;

public sealed class MigrationAssembliesOptions
{
    private readonly List<Assembly> _assemblies = [];

    public IReadOnlyList<Assembly> Assemblies => _assemblies.AsReadOnly();

    public void Add(Assembly assembly)
    {
        _assemblies.Add(assembly);
    }
}
