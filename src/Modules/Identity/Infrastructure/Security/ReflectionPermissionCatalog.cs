namespace Stratum.Modules.Identity.Infrastructure.Security;

using System.Reflection;
using Stratum.Common.Abstractions.Security;

internal sealed class ReflectionPermissionCatalog : IPermissionCatalog
{
    private readonly Lazy<IReadOnlyList<PermissionCatalogEntry>> _entries = new(Scan);

    public IReadOnlyList<PermissionCatalogEntry> GetAll() => _entries.Value;

    private static List<PermissionCatalogEntry> Scan()
    {
        var entries = new List<PermissionCatalogEntry>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            // Liakont: scan AUSSI les assemblies du produit consommateur (Liakont.*),
            // pas seulement le socle (Stratum.*), pour découvrir LiakontPermissions.
            var name = assembly.FullName;
            var isScannable = name is not null
                && (name.StartsWith("Stratum.", StringComparison.Ordinal)
                    || name.StartsWith("Liakont.", StringComparison.Ordinal));
            if (!isScannable)
            {
                continue;
            }

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t is not null).ToArray()!;
            }

            foreach (var type in types)
            {
                if (!type.IsClass || !type.IsAbstract || !type.IsSealed)
                {
                    continue;
                }

                if (!type.Name.EndsWith("Permissions", StringComparison.Ordinal))
                {
                    continue;
                }

                var moduleName = DeriveModuleName(type);

                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    if (field is { IsLiteral: true, FieldType.Name: "String" }
                        && field.GetRawConstantValue() is string value)
                    {
                        entries.Add(new PermissionCatalogEntry(moduleName, value));
                    }
                }
            }
        }

        return entries.OrderBy(e => e.Module).ThenBy(e => e.Permission).ToList();
    }

    private static string DeriveModuleName(Type type)
    {
        var ns = type.Namespace ?? string.Empty;
        const string prefix = "Stratum.Modules.";
        if (ns.StartsWith(prefix, StringComparison.Ordinal))
        {
            var rest = ns[prefix.Length..];
            var dot = rest.IndexOf('.');
            return dot > 0 ? rest[..dot] : rest;
        }

        // Liakont: les permissions du produit consommateur (ns Liakont.*, ex. Liakont.Host.Security)
        // sont regroupées sous le module « Liakont ».
        if (ns.StartsWith("Liakont.", StringComparison.Ordinal))
        {
            return "Liakont";
        }

        return type.Name.Replace("Permissions", string.Empty, StringComparison.Ordinal);
    }
}
