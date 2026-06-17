namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components.Pages;
using Liakont.Host.Signatures;
using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.DocumentApproval.Contracts.DTOs;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Security;
using Xunit;

/// <summary>
/// Tests bUnit de la page console des signatures (SIG10, P1 review n°19). Vérifient que la page est
/// PRÉSENTATIONNELLE : elle délègue lecture (ISignatureConsoleQueries) et écriture (ISignatureConsoleActions),
/// masque les actions sans liakont.actions, rend le statut/historique/preuve et valide la saisie. Les fakes
/// remplacent les services in-process (aucune base, aucun module réel).
/// </summary>
public sealed class SignaturesTests : BunitContext
{
    private static readonly Guid DocId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly FakeSignatureConsoleQueries _queries = new();
    private readonly FakeSignatureConsoleActions _actions = new();

    public SignaturesTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();

        // Par défaut : pas de permission d'action (la page reste consultable) + fakes in-process.
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(canAct: false));
        Services.AddScoped<ISignatureConsoleQueries>(_ => _queries);
        Services.AddScoped<ISignatureConsoleActions>(_ => _actions);
    }

    [Fact]
    public void Should_Render_Title_Lookup_Form_And_Providers_Section()
    {
        var cut = Render<Signatures>();

        cut.FindAll("[data-testid='signatures-title']").Should().ContainSingle();
        cut.FindAll("[data-testid='signatures-lookup']").Should().ContainSingle();
        cut.FindAll("[data-testid='signatures-purpose']").Should().ContainSingle();
        cut.FindAll("[data-testid='signatures-document-id']").Should().ContainSingle();
        cut.FindAll("[data-testid='signatures-lookup-submit']").Should().ContainSingle();
        cut.FindAll("[data-testid='signatures-providers']").Should().ContainSingle();

        // Aucun statut tant qu'aucune recherche n'a été lancée.
        cut.FindAll("[data-testid='signatures-status']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Reject_An_Invalid_Document_Id_Without_Querying()
    {
        var cut = Render<Signatures>();

        cut.Find("[data-testid='signatures-document-id']").Change("pas-un-guid");
        cut.Find("[data-testid='signatures-lookup-submit']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='signatures-input-error']").Should().ContainSingle();
        });
        _queries.GetStatusCalls.Should().BeEmpty("aucune lecture n'est tentée sur un identifiant invalide");
        cut.FindAll("[data-testid='signatures-status']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Show_Status_And_History_When_A_Validation_Exists()
    {
        _queries.View = new SignatureStatusView
        {
            Latest = BuildValidation(state: "PendingValidation", attempt: 1, isTerminal: false),
            Log = [BuildLogEntry(fromState: null, toState: "PendingValidation", attempt: 1)],
        };

        var cut = Render<Signatures>();
        Lookup(cut);

        cut.WaitForAssertion(() =>
        {
            _queries.GetStatusCalls.Should().ContainSingle().Which.DocumentId.Should().Be(DocId);
            cut.Find("[data-testid='signatures-state']").TextContent.Should().Contain("En attente de validation");
            cut.Find("[data-testid='signatures-status-attempt']").TextContent.Should().Contain("1");
            cut.FindAll("[data-testid='signatures-history-table']").Should().ContainSingle();
        });
    }

    [Fact]
    public void Should_Show_No_Validation_Message_When_No_Attempt()
    {
        _queries.View = new SignatureStatusView { Latest = null, Log = [] };

        var cut = Render<Signatures>();
        Lookup(cut);

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='signatures-status-none']").Should().ContainSingle();
            cut.FindAll("[data-testid='signatures-history-none']").Should().ContainSingle();
        });
    }

    [Fact]
    public void Should_Show_Error_Banner_When_Load_Throws()
    {
        _queries.Throws = true;

        var cut = Render<Signatures>();
        Lookup(cut);

        // L'échec reste VISIBLE (bandeau) et n'expose pas de statut (anti faux-vert).
        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='signatures-error']").Should().ContainSingle();
            cut.FindAll("[data-testid='signatures-status']").Should().BeEmpty();
        });
    }

    [Fact]
    public void Should_Hide_Action_Buttons_Without_Actions_Permission()
    {
        _queries.View = new SignatureStatusView
        {
            Latest = BuildValidation(state: "PendingValidation", attempt: 1, isTerminal: false),
            Log = [],
        };

        var cut = Render<Signatures>();
        Lookup(cut);

        cut.WaitForAssertion(() =>
        {
            // La fiche de statut reste visible en lecture, mais aucune action n'est proposée.
            cut.FindAll("[data-testid='signatures-status']").Should().ContainSingle();
            cut.FindAll("[data-testid='signatures-actions']").Should().BeEmpty();
            cut.FindAll("[data-testid='signatures-action-record']").Should().BeEmpty();
            cut.FindAll("[data-testid='signatures-action-contest']").Should().BeEmpty();
        });
    }

    [Fact]
    public void Should_Offer_Record_And_Contest_On_A_Pending_Validation_With_Actions_Permission()
    {
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(canAct: true));
        _queries.View = new SignatureStatusView
        {
            Latest = BuildValidation(state: "PendingValidation", attempt: 1, isTerminal: false),
            Log = [],
        };

        var cut = Render<Signatures>();
        Lookup(cut);

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='signatures-action-record']").Should().ContainSingle();
            cut.FindAll("[data-testid='signatures-action-contest']").Should().ContainSingle();

            // Genèse interdite quand une tentative non terminale existe.
            cut.FindAll("[data-testid='signatures-action-request']").Should().BeEmpty();
        });
    }

    [Fact]
    public void Record_Click_Calls_The_Action_Service_And_Reloads()
    {
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(canAct: true));
        _queries.View = new SignatureStatusView
        {
            Latest = BuildValidation(state: "PendingValidation", attempt: 1, isTerminal: false),
            Log = [],
        };
        _actions.RecordResult = SignatureActionResult.Ok("Acceptation enregistrée : le document est validé.");

        var cut = Render<Signatures>();
        Lookup(cut);

        var loadsBefore = _queries.GetStatusCalls.Count;
        cut.Find("[data-testid='signatures-action-record']").Click();

        cut.WaitForAssertion(() =>
        {
            _actions.RecordCalls.Should().ContainSingle().Which.Should().Be((DocId, ValidationPurpose.SelfBilledAcceptance));
            _queries.GetStatusCalls.Count.Should().BeGreaterThan(loadsBefore, "la page recharge le statut après l'action");
            cut.Find("[data-testid='signatures-action-feedback']").TextContent.Should().Contain("validé");
        });
    }

    [Fact]
    public void Contest_Click_Calls_The_Contest_Action()
    {
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(canAct: true));
        _queries.View = new SignatureStatusView
        {
            Latest = BuildValidation(state: "PendingValidation", attempt: 1, isTerminal: false),
            Log = [],
        };

        var cut = Render<Signatures>();
        Lookup(cut);

        cut.Find("[data-testid='signatures-action-contest']").Click();

        cut.WaitForAssertion(() =>
            _actions.ContestCalls.Should().ContainSingle().Which.Should().Be((DocId, ValidationPurpose.SelfBilledAcceptance)));
    }

    [Fact]
    public void Request_Click_Triggers_A_Genesis_When_No_Attempt_Exists()
    {
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(canAct: true));
        _queries.View = new SignatureStatusView { Latest = null, Log = [] };

        var cut = Render<Signatures>();
        Lookup(cut);

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='signatures-action-request']").Should().ContainSingle());
        cut.Find("[data-testid='signatures-action-request']").Click();

        cut.WaitForAssertion(() =>
            _actions.RequestCalls.Should().ContainSingle().Which.DocumentId.Should().Be(DocId));
    }

    [Fact]
    public void Should_List_Configured_Providers_When_Present()
    {
        _queries.ProviderTypes = ["Yousign"];

        var cut = Render<Signatures>();

        cut.FindAll("[data-testid='signatures-providers-list']").Should().ContainSingle();
        cut.Find("[data-testid='signatures-providers-list']").TextContent.Should().Contain("Yousign");
        cut.FindAll("[data-testid='signatures-providers-none']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Show_No_Providers_Message_When_None_Configured()
    {
        var cut = Render<Signatures>();

        cut.FindAll("[data-testid='signatures-providers-none']").Should().ContainSingle();
        cut.FindAll("[data-testid='signatures-providers-list']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Render_Proof_Table_With_Slot_ProofId_And_Pending_Slot()
    {
        _queries.View = new SignatureStatusView
        {
            Latest = new DocumentValidationDto
            {
                DocumentId = DocId,
                Purpose = ValidationPurpose.SelfBilledAcceptance,
                Attempt = 1,
                State = "PendingValidation",
                ProofLevel = "None",
                ExpressAcceptanceRecorded = false,
                IsTerminal = false,
                Slots =
                [
                    new ApprovalSlotDto { SignerId = "signataire-1", State = "Approved", ProofLevel = "SES", ProofId = "worm://preuve/abc123" },
                    new ApprovalSlotDto { SignerId = "signataire-2", State = "Pending", ProofLevel = "None", ProofId = null },
                ],
            },
            Log = [],
        };

        var cut = Render<Signatures>();
        Lookup(cut);

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='signatures-proof-table']").Should().ContainSingle();
            cut.Find("[data-testid='signatures-proof-table']").TextContent.Should().Contain("worm://preuve/abc123");
            cut.Find("[data-testid='signatures-proof-table']").TextContent.Should().Contain("En attente");
        });
    }

    [Fact]
    public void Should_Show_Error_Feedback_When_Action_Fails()
    {
        Services.AddScoped<IPermissionService>(_ => new FakePermissionService(canAct: true));
        _queries.View = new SignatureStatusView
        {
            Latest = BuildValidation(state: "PendingValidation", attempt: 1, isTerminal: false),
            Log = [],
        };
        _actions.RecordResult = SignatureActionResult.Failure("Refus simulé du service.");

        var cut = Render<Signatures>();
        Lookup(cut);
        cut.Find("[data-testid='signatures-action-record']").Click();

        cut.WaitForAssertion(() =>
        {
            var feedback = cut.Find("[data-testid='signatures-action-feedback']");
            feedback.GetAttribute("class").Should().Contain("liakont-signatures__feedback--error");
            feedback.TextContent.Should().Contain("Refus simulé");
        });
    }

    private static void Lookup(IRenderedComponent<Signatures> cut)
    {
        cut.Find("[data-testid='signatures-document-id']").Change(DocId.ToString());
        cut.Find("[data-testid='signatures-lookup-submit']").Click();
    }

    private static DocumentValidationDto BuildValidation(string state, int attempt, bool isTerminal) => new()
    {
        DocumentId = DocId,
        Purpose = ValidationPurpose.SelfBilledAcceptance,
        Attempt = attempt,
        State = state,
        ProofLevel = "None",
        ExpressAcceptanceRecorded = false,
        IsTerminal = isTerminal,
    };

    private static DocumentApprovalLogEntryDto BuildLogEntry(string? fromState, string toState, int attempt) => new()
    {
        DocumentId = DocId,
        Purpose = ValidationPurpose.SelfBilledAcceptance,
        Attempt = attempt,
        FromState = fromState,
        ToState = toState,
        OccurredAt = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
    };

    private sealed class FakeSignatureConsoleQueries : ISignatureConsoleQueries
    {
        public SignatureStatusView View { get; set; } = new();

        public bool Throws { get; set; }

        public IReadOnlyCollection<string> ProviderTypes { get; set; } = [];

        public List<(Guid DocumentId, ValidationPurpose Purpose)> GetStatusCalls { get; } = [];

        public Task<SignatureStatusView> GetStatusAsync(Guid documentId, ValidationPurpose purpose, CancellationToken cancellationToken = default)
        {
            GetStatusCalls.Add((documentId, purpose));
            if (Throws)
            {
                throw new InvalidOperationException("Échec simulé de chargement du statut de validation.");
            }

            return Task.FromResult(View);
        }

        public IReadOnlyCollection<string> GetConfiguredProviderTypes() => ProviderTypes;
    }

    private sealed class FakeSignatureConsoleActions : ISignatureConsoleActions
    {
        public List<(Guid DocumentId, ValidationPurpose Purpose, DateTimeOffset? Deadline)> RequestCalls { get; } = [];

        public List<(Guid DocumentId, ValidationPurpose Purpose)> RecordCalls { get; } = [];

        public List<(Guid DocumentId, ValidationPurpose Purpose)> ContestCalls { get; } = [];

        public SignatureActionResult RequestResult { get; set; } = SignatureActionResult.Ok("Demande déclenchée.");

        public SignatureActionResult RecordResult { get; set; } = SignatureActionResult.Ok("Acceptation enregistrée.");

        public SignatureActionResult ContestResult { get; set; } = SignatureActionResult.Ok("Contestation enregistrée.");

        public Task<SignatureActionResult> RequestValidationAsync(Guid documentId, ValidationPurpose purpose, DateTimeOffset? deadlineUtc, CancellationToken cancellationToken = default)
        {
            RequestCalls.Add((documentId, purpose, deadlineUtc));
            return Task.FromResult(RequestResult);
        }

        public Task<SignatureActionResult> RecordRecordedAsync(Guid documentId, ValidationPurpose purpose, CancellationToken cancellationToken = default)
        {
            RecordCalls.Add((documentId, purpose));
            return Task.FromResult(RecordResult);
        }

        public Task<SignatureActionResult> ContestAsync(Guid documentId, ValidationPurpose purpose, CancellationToken cancellationToken = default)
        {
            ContestCalls.Add((documentId, purpose));
            return Task.FromResult(ContestResult);
        }
    }

    private sealed class FakePermissionService : IPermissionService
    {
        private readonly bool _canAct;

        public FakePermissionService(bool canAct) => _canAct = canAct;

        public event Action? OnPermissionsChanged
        {
            add { }
            remove { }
        }

        public bool HasPermission(string permission) =>
            _canAct && string.Equals(permission, "liakont.actions", StringComparison.Ordinal);
    }
}
