namespace Liakont.PaClients.B2Brouter.Wire;

/// <summary>Une erreur B2Brouter (code + message), conservée intacte pour la piste d'audit (F05 §3).</summary>
internal sealed record B2BrouterError
{
    /// <summary>Code d'erreur tel que retourné par B2Brouter.</summary>
    public string? Code { get; init; }

    /// <summary>Message d'erreur tel que retourné par B2Brouter.</summary>
    public string? Message { get; init; }
}
