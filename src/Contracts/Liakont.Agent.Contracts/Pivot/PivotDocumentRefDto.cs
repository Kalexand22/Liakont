namespace Liakont.Agent.Contracts.Pivot;

using System;

/// <summary>
/// Référence à un document d'origine (facture rectifiée par un avoir) — EN 16931 BT-25.
/// La DATE d'origine est OBLIGATOIRE (F07-F08 §B.3 : liste de { Number, IssueDate } ; B2Brouter
/// exige <c>amended_number</c> + <c>amended_date</c> — F05). Une simple liste de chaînes perdrait
/// cette date obligatoire — d'où ce type dédié (acceptance PIV01).
/// </summary>
public sealed class PivotDocumentRefDto
{
    /// <summary>Crée une référence de document d'origine.</summary>
    /// <param name="number">Numéro du document d'origine (EN 16931 BT-25). Obligatoire.</param>
    /// <param name="issueDate">Date d'émission du document d'origine. Obligatoire (F07-F08 §B.3).</param>
    /// <param name="sourceReference">Référence du document d'origine dans le système source.</param>
    public PivotDocumentRefDto(string number, DateTime issueDate, string? sourceReference = null)
    {
        Number = number;
        IssueDate = issueDate;
        SourceReference = sourceReference;
    }

    /// <summary>Numéro du document d'origine (EN 16931 BT-25).</summary>
    public string Number { get; }

    /// <summary>Date d'émission du document d'origine (obligatoire).</summary>
    public DateTime IssueDate { get; }

    /// <summary>Référence du document d'origine dans le système source (traçabilité).</summary>
    public string? SourceReference { get; }
}
