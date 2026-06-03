namespace Stratum.Common.UI.Models;

/// <summary>
/// A dynamic variable that can be inserted into an email template body or subject.
/// </summary>
public sealed record EmailTemplateVariable
{
    public required string Name { get; init; }

    public string? SampleValue { get; init; }
}
