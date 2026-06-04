namespace Liakont.Modules.TenantSettings.Contracts.Queries;

using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Stratum.Common.Abstractions.Messaging;

/// <summary>Lit le profil du tenant courant (F12-A §2), ou <c>null</c> s'il n'est pas encore défini.</summary>
public record GetTenantProfileQuery : IQuery<TenantProfileDto?>;
