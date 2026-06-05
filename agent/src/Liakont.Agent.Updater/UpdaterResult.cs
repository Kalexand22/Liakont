namespace Liakont.Agent.Updater;

/// <summary>Résultat d'un cycle d'updater : l'issue + un message opérateur français.</summary>
public sealed class UpdaterResult
{
    /// <summary>Crée un résultat d'updater.</summary>
    /// <param name="outcome">L'issue du cycle.</param>
    /// <param name="message">Message opérateur (français).</param>
    public UpdaterResult(UpdaterOutcome outcome, string message)
    {
        Outcome = outcome;
        Message = message ?? string.Empty;
    }

    /// <summary>L'issue du cycle.</summary>
    public UpdaterOutcome Outcome { get; }

    /// <summary>Message opérateur en français.</summary>
    public string Message { get; }

    /// <summary>Vrai si la nouvelle version a été appliquée et a redémarré sainement.</summary>
    public bool Succeeded => Outcome == UpdaterOutcome.Applied;
}
