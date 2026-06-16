namespace Liakont.Modules.Signature.Contracts;

/// <summary>
/// LOCALISATION de la signature (ADR-0027 §2 ; F17 §2). <c>[Flags]</c> avec des valeurs en puissances
/// de deux distinctes et <c>None = 0</c> : un <c>[Flags]</c> dont une valeur vaudrait 0 rendrait
/// <c>HasFlag</c> toujours vrai pour cette valeur (bug C# classique — INV-SIGPROV-2). Axe ORTHOGONAL à
/// <see cref="CompletionTransport"/> (un fournisseur distant <em>polling-only</em> ou un capteur sur
/// place asynchrone existent — le transport n'est jamais déduit de la localisation).
/// </summary>
[Flags]
public enum SignatureMode
{
    /// <summary>Aucun mode déclaré.</summary>
    None = 0,

    /// <summary>Signature À DISTANCE (server-side, ex. Yousign — SIG07).</summary>
    Remote = 1,

    /// <summary>Signature SUR PLACE (capteur desktop, ex. Wacom — SIG08).</summary>
    OnSite = 2,
}
