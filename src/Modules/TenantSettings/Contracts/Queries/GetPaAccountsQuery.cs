namespace Liakont.Modules.TenantSettings.Contracts.Queries;

using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Stratum.Common.Abstractions.Messaging;

/// <summary>Liste les comptes Plateforme Agréée du tenant courant (F12-A §4), sans jamais exposer les clés API.</summary>
public record GetPaAccountsQuery : IQuery<IReadOnlyList<PaAccountDto>>;
