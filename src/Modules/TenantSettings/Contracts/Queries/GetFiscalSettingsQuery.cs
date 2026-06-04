namespace Liakont.Modules.TenantSettings.Contracts.Queries;

using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Stratum.Common.Abstractions.Messaging;

/// <summary>Lit le paramétrage fiscal du tenant courant (F12-A §3), ou <c>null</c> s'il n'est pas encore défini.</summary>
public record GetFiscalSettingsQuery : IQuery<FiscalSettingsDto?>;
