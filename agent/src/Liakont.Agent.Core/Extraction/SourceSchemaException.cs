namespace Liakont.Agent.Core.Extraction;

using System;
using System.Runtime.Serialization;

/// <summary>
/// Le schéma de la source est incompatible (table/colonne attendue absente, version non supportée) :
/// échec FATAL, NON réessayable (F01-F02 §4.2 R7). Une intervention est requise (mise à jour de
/// l'adaptateur, correction de la base). Signalé au heartbeat, jamais repris en boucle.
/// <para>
/// CONTRAINTE DE SÉCURITÉ (CLAUDE.md n°10) : le <see cref="System.Exception.Message"/> est remonté à
/// la plateforme (heartbeat <c>LastError</c>, AGT03) et persisté/journalisé localement. Les
/// implémentations d'<c>IExtractor</c> (lot ADP) NE DOIVENT JAMAIS y inclure de secret — en
/// particulier la chaîne de connexion ODBC (mot de passe). Décrire la cause, jamais les identifiants.
/// </para>
/// </summary>
[Serializable]
public class SourceSchemaException : Exception
{
    /// <summary>Crée une exception « schéma de source incompatible ».</summary>
    public SourceSchemaException()
    {
    }

    /// <summary>Crée une exception « schéma de source incompatible » avec un message opérateur français.</summary>
    /// <param name="message">Message décrivant l'incompatibilité et l'action corrective.</param>
    public SourceSchemaException(string message)
        : base(message)
    {
    }

    /// <summary>Crée une exception « schéma de source incompatible » avec un message et une cause.</summary>
    /// <param name="message">Message décrivant l'incompatibilité et l'action corrective.</param>
    /// <param name="innerException">Cause technique sous-jacente.</param>
    public SourceSchemaException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Constructeur de désérialisation.</summary>
    /// <param name="info">Conteneur de sérialisation.</param>
    /// <param name="context">Contexte de flux.</param>
    protected SourceSchemaException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
    }
}
