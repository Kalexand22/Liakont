namespace Liakont.Tests.E2E.Support;

using System;

/// <summary>
/// Marque une méthode de test E2E comme PREUVE que la garde d'une permission sensible
/// (<c>liakont.actions</c>/<c>liakont.settings</c>) est exercée avec un rôle realm NON super-admin.
/// </summary>
/// <remarks>
/// Garde CI de RDF10 : le trou IDN01 venait de tests joués en super-admin (qui court-circuite la garde),
/// si bien que le permission-gating n'était jamais réellement exercé. Cet attribut, agrégé par
/// <see cref="SensitivePermissionE2ECoverageTests"/>, impose ≥ 1 E2E par permission sensible avec un
/// rôle non super-admin — il ne peut donc plus disparaître ni régresser silencieusement.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class SensitivePermissionCoverageAttribute : Attribute
{
    public SensitivePermissionCoverageAttribute(string permission, string role)
    {
        Permission = permission;
        Role = role;
    }

    /// <summary>Permission sensible exercée (valeur de <c>LiakontPermissions</c>).</summary>
    public string Permission { get; }

    /// <summary>Username du rôle realm E2E non super-admin sous lequel le test se connecte.</summary>
    public string Role { get; }
}
