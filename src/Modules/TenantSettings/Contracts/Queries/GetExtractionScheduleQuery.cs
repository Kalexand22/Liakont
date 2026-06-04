namespace Liakont.Modules.TenantSettings.Contracts.Queries;

using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Stratum.Common.Abstractions.Messaging;

/// <summary>Lit la planification d'extraction du tenant courant (F12-A §5), ou <c>null</c> si non définie.</summary>
public record GetExtractionScheduleQuery : IQuery<ExtractionScheduleDto?>;
