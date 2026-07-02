namespace Liakont.Modules.Ged.Contracts.Consultation;

using System;

/// <summary>
/// Levée par <see cref="IConsultationAuditWriter"/> en régime <see cref="ConsultationAuditMode.Evidential"/>
/// lorsque la trace de consultation ne peut être écrite (fail-closed, ADR-0036 §3). L'appelant la traduit en
/// REFUS d'accès (message FR opérateur). N'est JAMAIS levée en régime <see cref="ConsultationAuditMode.BestEffort"/>
/// (défaut), où l'échec de trace est journalisé en Warning sans casser la lecture.
/// </summary>
public sealed class ConsultationAuditException : Exception
{
    public ConsultationAuditException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
