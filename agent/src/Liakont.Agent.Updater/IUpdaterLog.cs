namespace Liakont.Agent.Updater;

/// <summary>Journal de l'updater (messages opérateur français, CLAUDE.md n°12). Best-effort : ne lève jamais.</summary>
public interface IUpdaterLog
{
    /// <summary>Écrit une ligne horodatée.</summary>
    /// <param name="message">Message à journaliser.</param>
    void Write(string message);
}
