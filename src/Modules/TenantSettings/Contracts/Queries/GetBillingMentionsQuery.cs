namespace Liakont.Modules.TenantSettings.Contracts.Queries;

using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Stratum.Common.Abstractions.Messaging;

/// <summary>Lit les mentions de facturation du tenant courant (F12-A §3.4), ou <c>null</c> si non définies.</summary>
public record GetBillingMentionsQuery : IQuery<BillingMentionsDto?>;
