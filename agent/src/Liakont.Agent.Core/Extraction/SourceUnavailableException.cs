namespace Liakont.Agent.Core.Extraction;

using System;
using System.Runtime.Serialization;

/// <summary>
/// La source est momentanément indisponible (connexion ODBC coupée, base verrouillée, réseau) :
/// échec RÉESSAYABLE (F01-F02 §4.2 R7). Le run échoue mais sera repris au cycle suivant — aucune
/// intervention requise. À distinguer de <see cref="SourceSchemaException"/> (fatale).
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
