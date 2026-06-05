namespace Liakont.Agent.Core.Extraction;

using System;
using System.Runtime.Serialization;

/// <summary>
/// La source est momentanément indisponible (connexion ODBC coupée, base verrouillée, réseau) :
/// échec RÉESSAYABLE (F01-F02 §4.2 R7). Le run échoue mais sera repris au cycle suivant — aucune
/// intervention requise. À distinguer de <see cref="SourceSchemaException"/> (fatale).
/// <para>
/// CONTRAINTE DE SÉCURITÉ (CLAUDE.md n°10) : le <see cref="System.Exception.Message"/> est remonté à
/// la plateforme (heartbeat <c>LastError</c>, AGT03) et persisté/journalisé localement. Les
/// implémentations d'<c>IExtractor</c> (lot ADP) NE DOIVENT JAMAIS y inclure de secret — en
/// particulier la chaîne de connexion ODBC (mot de passe). Décrire la cause, jamais les identifiants.
/// </para>
/// </summary>
[Serializable]
public class SourceUnavailableException : Exception
{
    /// <summary>Crée une exception « source indisponible ».</summary>
    public SourceUnavailableException()
    {
    }

    /// <summary>Crée une exception « source indisponible » avec un message opérateur français.</summary>
    /// <param name="message">Message décrivant l'indisponibilité.</param>
    public SourceUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Crée une exception « source indisponible » avec un message et une cause.</summary>
    /// <param name="message">Message décrivant l'indisponibilité.</param>
    /// <param name="innerException">Cause technique sous-jacente.</param>
    public SourceUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Constructeur de désérialisation.</summary>
    /// <param name="info">Conteneur de sérialisation.</param>
    /// <param name="context">Contexte de flux.</param>
    protected SourceUnavailableException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
    }
}
