namespace Liakont.PaClients.SuperPdp.Wire;

/// <summary>Une erreur Super PDP (code + message), conservée intacte pour la piste d'audit (F14 §4.1).</summary>
internal sealed record SuperPdpError
{
    /// <summary>Code d'erreur tel que retourné par Super PDP.</summary>
    public string? Code { get; init; }

    /// <summary>Message d'erreur tel que retourné par Super PDP.</summary>
    public string? Message { get; init; }
}
