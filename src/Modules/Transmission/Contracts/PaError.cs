namespace Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Une erreur retournée par la PA, conservée INTACTE pour la piste d'audit (F05 §3 : code + message).
/// Les erreurs silencieuses (réponse 200 + <c>errors[]</c> non vide) remontent ici sans être perdues.
/// </summary>
/// <param name="Code">Code d'erreur tel que retourné par la PA.</param>
/// <param name="Message">Message d'erreur tel que retourné par la PA.</param>
public sealed record PaError(string Code, string Message);
