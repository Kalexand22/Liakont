namespace Liakont.Modules.Validation.Tests.Unit;

using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Validation.Contracts;
using Liakont.Modules.Validation.Domain;
using Xunit;

public sealed class ValidationPipelineTests
{
    [Fact]
    public async Task Conforme_document_with_no_rule_issue_is_valid()
    {
        var pipeline = new ValidationPipeline(new IDocumentRule[]
        {
            new StubRule("R1"),
            new StubRule("R2"),
        });

        var result = await pipeline.ValidateAsync(Context());

        result.IsValid.Should().BeTrue();
        result.HasBlockingIssue.Should().BeFalse();
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Aggregates_issues_from_all_rules()
    {
        var pipeline = new ValidationPipeline(new IDocumentRule[]
        {
            new StubRule("R1", ValidationIssue.Warning("W1", "Alerte 1.")),
            new StubRule("R2", ValidationIssue.Blocking("B1", "Blocage 1."), ValidationIssue.Warning("W2", "Alerte 2.")),
        });

        var result = await pipeline.ValidateAsync(Context());

        result.Issues.Should().HaveCount(3);
        var codes = result.Issues.Select(issue => issue.Code).ToList();
        codes.Should().Contain("W1");
        codes.Should().Contain("B1");
        codes.Should().Contain("W2");
        result.HasBlockingIssue.Should().BeTrue();
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Warning_only_document_stays_valid()
    {
        var pipeline = new ValidationPipeline(new IDocumentRule[]
        {
            new StubRule("R1", ValidationIssue.Warning("W1", "Alerte source.")),
        });

        var result = await pipeline.ValidateAsync(Context());

        result.IsValid.Should().BeTrue();
        result.Issues.Should().ContainSingle().Which.Severity.Should().Be(ValidationSeverity.Warning);
    }

    [Fact]
    public async Task Crashing_rule_produces_blocking_rule_crashed_issue_and_other_rules_still_run()
    {
        var pipeline = new ValidationPipeline(new IDocumentRule[]
        {
            new ThrowingRule("BOOM"),
            new StubRule("R2", ValidationIssue.Warning("W2", "Alerte après crash.")),
        });

        var result = await pipeline.ValidateAsync(Context());

        result.IsValid.Should().BeFalse();
        var crashed = result.Issues.Should().ContainSingle(issue => issue.Code == ValidationIssueCodes.RuleCrashed).Subject;
        crashed.Severity.Should().Be(ValidationSeverity.Blocking);
        crashed.MessageOperateur.Should().Contain("2019"); // le numéro de document est cité
        crashed.DetailTechnique.Should().Contain("BOOM"); // le détail technique journalise l'exception
        result.Issues.Should().Contain(issue => issue.Code == "W2"); // les autres règles s'exécutent quand même
    }

    [Fact]
    public async Task Rule_returning_null_is_handled_gracefully()
    {
        var pipeline = new ValidationPipeline(new IDocumentRule[] { new NullReturningRule("N1") });

        var result = await pipeline.ValidateAsync(Context());

        result.IsValid.Should().BeTrue();
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancellation_is_propagated_not_swallowed()
    {
        var pipeline = new ValidationPipeline(new IDocumentRule[] { new StubRule("R1") });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await pipeline.ValidateAsync(Context(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Rule_timeout_when_token_not_cancelled_produces_blocking_rule_crashed()
    {
        // Une règle lève TaskCanceledException (dérive de OperationCanceledException) due à un
        // timeout aval (HttpClient, commande DB) alors que le token appelant n'est PAS annulé.
        // Le pipeline doit traiter ça comme un crash de règle (RULE_CRASHED), jamais propager.
        var pipeline = new ValidationPipeline(new IDocumentRule[] { new TimeoutRule() });

        var result = await pipeline.ValidateAsync(Context(), CancellationToken.None);

        result.HasBlockingIssue.Should().BeTrue();
        result.Issues.Should().ContainSingle(issue => issue.Code == ValidationIssueCodes.RuleCrashed);
    }

    [Fact]
    public async Task ValidateMappingIndependent_runs_only_rules_independent_of_tva_mapping()
    {
        // FIX06 : le sous-ensemble n'exécute QUE les règles indépendantes du mapping (DependsOnTvaMapping == false).
        var independent = new StubRule("INDEP", ValidationIssue.Blocking("B_INDEP", "Motif indépendant."));
        var dependent = new StubRule("DEP", dependsOnTvaMapping: true, ValidationIssue.Blocking("B_DEP", "Motif dépendant du mapping."));
        var pipeline = new ValidationPipeline(new IDocumentRule[] { independent, dependent });

        var result = await pipeline.ValidateMappingIndependentAsync(Context());

        var codes = result.Issues.Select(issue => issue.Code).ToList();
        codes.Should().Contain("B_INDEP");
        codes.Should().NotContain("B_DEP", "une règle dépendante du mapping est exclue de l'agrégation indépendante");
    }

    [Fact]
    public async Task ValidateMappingIndependent_keeps_full_validation_unchanged()
    {
        // Garde anti-affaiblissement : ValidateAsync reste COMPLET (toutes les règles), y compris les dépendantes.
        var independent = new StubRule("INDEP", ValidationIssue.Blocking("B_INDEP", "Motif indépendant."));
        var dependent = new StubRule("DEP", dependsOnTvaMapping: true, ValidationIssue.Blocking("B_DEP", "Motif dépendant du mapping."));
        var pipeline = new ValidationPipeline(new IDocumentRule[] { independent, dependent });

        var result = await pipeline.ValidateAsync(Context());

        var codes = result.Issues.Select(issue => issue.Code).ToList();
        codes.Should().Contain("B_INDEP");
        codes.Should().Contain("B_DEP", "la validation complète exécute TOUTES les règles (rien n'est affaibli)");
    }

    [Fact]
    public void Empty_rule_set_is_allowed()
    {
        var act = () => new ValidationPipeline(Array.Empty<IDocumentRule>());

        act.Should().NotThrow();
    }

    [Fact]
    public async Task Null_context_is_rejected()
    {
        var pipeline = new ValidationPipeline(Array.Empty<IDocumentRule>());

        var act = async () => await pipeline.ValidateAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    private static DocumentValidationContext Context()
    {
        var document = new PivotDocumentDto(
            sourceDocumentKind: "BORDEREAU",
            number: "2019",
            issueDate: new DateTime(2024, 1, 15),
            sourceReference: "src-2019",
            supplier: new PivotPartyDto("Étude Fictive SVV"),
            totals: new PivotTotalsDto(1160.00m, 0m, 1160.00m),
            operationCategory: OperationCategory.LivraisonBiens);
        return new DocumentValidationContext(document, Guid.NewGuid());
    }

    private sealed class StubRule : IDocumentRule
    {
        private readonly ValidationIssue[] _issues;

        public StubRule(string code, params ValidationIssue[] issues)
            : this(code, dependsOnTvaMapping: false, issues)
        {
        }

        public StubRule(string code, bool dependsOnTvaMapping, params ValidationIssue[] issues)
        {
            Code = code;
            DependsOnTvaMapping = dependsOnTvaMapping;
            _issues = issues;
        }

        public string Code { get; }

        public bool DependsOnTvaMapping { get; }

        public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(DocumentValidationContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ValidationIssue>>(_issues);
        }
    }

    private sealed class ThrowingRule : IDocumentRule
    {
        private readonly string _marker;

        public ThrowingRule(string marker)
        {
            _marker = marker;
        }

        public string Code => "THROWING_RULE";

        public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(DocumentValidationContext context, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(_marker);
        }
    }

    private sealed class TimeoutRule : IDocumentRule
    {
        public string Code => "TIMEOUT_RULE";

        public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(DocumentValidationContext context, CancellationToken cancellationToken = default)
        {
            // Simule un timeout HttpClient / commande DB : lève OperationCanceledException
            // alors que le token appelant n'est PAS annulé.
            throw new TaskCanceledException("Simulated downstream timeout.");
        }
    }

    private sealed class NullReturningRule : IDocumentRule
    {
        public NullReturningRule(string code)
        {
            Code = code;
        }

        public string Code { get; }

        public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(DocumentValidationContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ValidationIssue>>(null!);
        }
    }
}
