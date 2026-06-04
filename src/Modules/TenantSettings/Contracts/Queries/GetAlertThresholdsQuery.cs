namespace Liakont.Modules.TenantSettings.Contracts.Queries;

using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Stratum.Common.Abstractions.Messaging;

/// <summary>Lit les seuils d'alerte de supervision du tenant courant (F12-A §6), ou <c>null</c> si non définis.</summary>
public record GetAlertThresholdsQuery : IQuery<AlertThresholdsDto?>;
