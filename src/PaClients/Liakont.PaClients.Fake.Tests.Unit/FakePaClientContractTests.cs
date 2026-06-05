namespace Liakont.PaClients.Fake.Tests.Unit;

using Liakont.Modules.Transmission.Contracts;
using Liakont.PaClients.Contract.Tests;

/// <summary>
/// Exécute la suite de contrat commune (<see cref="PaClientContractTests"/>) contre le plug-in FACTICE
/// (PAA02) — la PREUVE que la suite est exécutable (PAA03) et que le plug-in livré avec le produit
/// honore le contrat PA. C'est aussi le PATRON que suivront les projets de test de B2Brouter (PAB04) et
/// Super PDP (PAS03) : référencer la base abstraite (sans plug-in) et la piloter via un mock HTTP
/// (testing-strategy §6).
/// </summary>
public sealed class FakePaClientContractTests : PaClientContractTests
{
    /// <inheritdoc />
    protected override IPaClient CreateClient(PaClientContractSetup setup)
    {
        var defaults = new FakePaClientOptions();
        return new FakePaClient(new FakePaClientOptions
        {
            SendScenario = MapScenario(setup.Outcome),
            Capabilities = setup.Capabilities ?? defaults.Capabilities,
            RejectionErrors = setup.RejectionErrors ?? defaults.RejectionErrors,
        });
    }

    private static FakePaScenario MapScenario(PaSendOutcome outcome) => outcome switch
    {
        PaSendOutcome.Success => FakePaScenario.Success,
        PaSendOutcome.Rejected => FakePaScenario.Rejected,
        PaSendOutcome.SilentError => FakePaScenario.SilentError,
        PaSendOutcome.TechnicalError => FakePaScenario.TechnicalError,
        PaSendOutcome.Timeout => FakePaScenario.Timeout,
        _ => throw new ArgumentOutOfRangeException(
            nameof(outcome), outcome, "Issue de contrat non mappée vers un scénario du plug-in factice."),
    };
}
