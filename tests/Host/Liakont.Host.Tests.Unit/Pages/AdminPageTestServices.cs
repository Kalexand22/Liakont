namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Stratum.Common.Abstractions.Collaboration;
using Stratum.Common.Abstractions.Grid;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.UI;

/// <summary>
/// Stubs partagés des tests bUnit de pages d'admin du SOCLE (RB6 P2). Centralise les doubles communs
/// (<c>ISender</c>/<c>IPermissionService</c>/<c>IGridPreferenceService</c>/<c>ISavedFilterService</c>/
/// <c>IStringLocalizer</c>/<c>IActorContextAccessor</c>) pour ne pas les redéclarer dans chaque page testée.
/// <c>AddCommonUI()</c> fournit le vrai <c>IBrowserTimeZone</c> (non résolu en bUnit → repli UTC déterministe),
/// donc une migration <c>&lt;LiakontDate&gt;</c> est testable sans JS. Chaque test ajoute en plus sa/ses
/// query(s) de module et ses permissions.
/// </summary>
internal static class AdminPageTestServices
{
    public static readonly Guid DefaultCompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static IServiceCollection AddAdminPageStubs(
        this IServiceCollection services,
        Guid? companyId = null,
        params string[] permissions)
    {
        services.AddLogging();
        services.AddCommonUI();
        services.AddSingleton<IStringLocalizer<SharedResources>>(new StubStringLocalizer());
        services.AddScoped<IGridPreferenceService>(_ => new NullGridPreferenceService());
        services.AddScoped<ISavedFilterService>(_ => new NullSavedFilterService());
        services.AddScoped<IPermissionService>(_ => new FakePermissionService(permissions));
        services.AddScoped<ISender>(_ => new NoopSender());
        services.AddScoped<IActorContextAccessor>(_ => new StubActorContextAccessor(companyId ?? DefaultCompanyId));

        // Mode View d'un DeclaredFormPage : rend EntityPresenceLayout (présence collaborative). AddCommonUI fournit
        // CircuitPresenceRegistry/IToastService mais PAS ces deux-là → stubs no-op.
        services.AddSingleton<ICollaborationService>(new StubCollaborationService());
        services.AddSingleton<IEntityChangeSubscriber>(new StubEntityChangeSubscriber());
        return services;
    }

    private sealed class NoopSender : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            Task.FromResult<TResponse>(default!);

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            Task.CompletedTask;

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            Task.FromResult<object?>(null);

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakePermissionService : IPermissionService
    {
        private readonly HashSet<string> _permissions;

        public FakePermissionService(params string[] permissions) =>
            _permissions = new HashSet<string>(permissions, StringComparer.Ordinal);

        public event Action? OnPermissionsChanged
        {
            add { }
            remove { }
        }

        public bool HasPermission(string permission) => _permissions.Contains(permission);
    }

    private sealed class NullSavedFilterService : ISavedFilterService
    {
        public Task<IReadOnlyList<SavedFilter>> ListAsync(Guid userId, string gridKey, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SavedFilter>>([]);

        public Task<SavedFilter?> GetAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<SavedFilter?>(null);

        public Task<SavedFilter> SaveAsync(SavedFilter filter, CancellationToken ct = default) =>
            Task.FromResult(filter);

        public Task DeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;

        public Task SetDefaultAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullGridPreferenceService : IGridPreferenceService
    {
        public Task<UserGridPreference?> GetPreferenceAsync(Guid userId, string gridKey, CancellationToken ct = default) =>
            Task.FromResult<UserGridPreference?>(null);

        public Task SavePreferenceAsync(Guid userId, string gridKey, IReadOnlyList<string> columnKeys, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SaveViewPreferenceAsync(Guid userId, string gridKey, ViewKind viewKind, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SaveFilterStateAsync(Guid userId, string gridKey, string? filterStateJson, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SaveColumnWidthsAsync(Guid userId, string gridKey, IReadOnlyDictionary<string, string> columnWidths, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class StubStringLocalizer : IStringLocalizer<SharedResources>
    {
        public LocalizedString this[string name] => new(name, name, resourceNotFound: true);

        public LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(CultureInfo.InvariantCulture, name, arguments), resourceNotFound: true);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    private sealed class StubCollaborationService : ICollaborationService
    {
        public event Action? OnPresenceChanged
        {
            add { }
            remove { }
        }

        public event Action? OnFieldPresenceChanged
        {
            add { }
            remove { }
        }

        public TimeSpan FieldLockTtl => TimeSpan.FromSeconds(60);

        public void Track(string entityType, string entityId, string circuitId, string user)
        {
        }

        public void Untrack(string circuitId)
        {
        }

        public IReadOnlyList<PresenceEntry> GetPresence(string entityType, string entityId) => [];

        public void SetFieldFocus(string circuitId, string entityType, string entityId, string fieldName, string user)
        {
        }

        public void ClearFieldFocus(string circuitId, string? fieldName = null)
        {
        }

        public IReadOnlyList<FieldFocusEntry> GetFieldPresence(string entityType, string entityId, string fieldName) => [];

        public string? IsFieldLocked(string entityType, string entityId, string fieldName, string circuitId) => null;

        public void RenewFieldFocus(string circuitId)
        {
        }

        public void PurgeExpiredEntries()
        {
        }
    }

    private sealed class StubEntityChangeSubscriber : IEntityChangeSubscriber
    {
        public void Subscribe(string circuitId, Action<EntityChangedEvent> callback)
        {
        }

        public void Unsubscribe(string circuitId)
        {
        }
    }

    private sealed class StubActorContextAccessor : IActorContextAccessor
    {
        public StubActorContextAccessor(Guid? companyId) => Current = new StubActorContext(companyId);

        public IActorContext Current { get; }

        private sealed class StubActorContext : IActorContext
        {
            public StubActorContext(Guid? companyId) => CompanyId = companyId;

            // Non vide : StratumDataGrid ne charge la préférence de colonnes (FakeGridPreferenceService, pour
            // exercer une colonne Date migrée mais defaultVisible:false) QUE si UserId != Guid.Empty.
            public Guid UserId => Guid.Parse("99999999-9999-9999-9999-999999999999");

            public Guid CorrelationId => Guid.Empty;

            public bool IsAuthenticated => true;

            public string? DisplayName => "Admin";

            public string? Email => null;

            public Guid? CompanyId { get; }

            public string? Timezone => null;

            public string? Language => "fr";

            public string? TenantId => "tenant-test";
        }
    }
}
