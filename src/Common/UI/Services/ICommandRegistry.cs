namespace Stratum.Common.UI.Services;

using Stratum.Common.UI.Models;

/// <summary>
/// Registry of semantic command definitions.
/// Components and pages register <see cref="CommandDefinition"/> objects here so that the
/// shortcut help dialog can discover all available commands regardless of scope.
/// </summary>
public interface ICommandRegistry
{
    /// <summary>Raised whenever the registry contents change.</summary>
    event Action? Changed;

    /// <summary>Registers or replaces a command definition.</summary>
    void Register(CommandDefinition definition);

    /// <summary>Removes a previously registered command definition.</summary>
    void Unregister(string commandId);

    /// <summary>Returns all currently registered command definitions.</summary>
    IReadOnlyList<CommandDefinition> GetAll();
}
