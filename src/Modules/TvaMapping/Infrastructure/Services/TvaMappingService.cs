namespace Liakont.Modules.TvaMapping.Infrastructure.Services;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.TvaMapping.Contracts.Services;
using Liakont.Modules.TvaMapping.Domain.Entities;
using Liakont.Modules.TvaMapping.Domain.Mapping;
using Liakont.Modules.TvaMapping.Domain.Services;

/// <summary>
/// Implémentation de <see cref="ITvaMappingService"/> : expose le moteur de domaine <see cref="TvaMapper"/>
/// à la frontière Contracts (consommé par le pipeline, PIP01b). N'INVENTE AUCUNE règle fiscale : pour
/// chaque requête de ligne EXPLICITE, il applique la table validée du tenant et remonte le résultat +
/// l'état de validation. La résolution part/code depuis une ligne pivot reste à l'appelant (PIP01b).
/// </summary>
internal sealed class TvaMappingService : ITvaMappingService
{
    private readonly IMappingTableSource _tableSource;
    private readonly TimeProvider _timeProvider;

    public TvaMappingService(IMappingTableSource tableSource)
        : this(tableSource, TimeProvider.System)
    {
    }

    internal TvaMappingService(IMappingTableSource tableSource, TimeProvider timeProvider)
    {
        _tableSource = tableSource;
        _timeProvider = timeProvider;
    }

    public async Task<DocumentTvaMappingResult> MapAsync(
        Guid companyId,
        IReadOnlyList<TvaLineMappingRequest> lines,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        MappingTable? table = await _tableSource.LoadAsync(companyId, cancellationToken);

        if (table is null)
        {
            return new DocumentTvaMappingResult
            {
                TableExists = false,
                IsValidated = false,
                MappingVersion = null,
                Lines = BlockAllForMissingTable(lines),
            };
        }

        var mappedAt = _timeProvider.GetUtcNow();
        var results = new List<TvaLineMappingResult>(lines.Count);
        foreach (var line in lines)
        {
            var request = new MappingRequest
            {
                SourceRegimeCode = line.SourceRegimeCode,
                Part = ToDomainPart(line.Part),
                SourceFlags = line.SourceFlags,
            };

            MappingResult result = TvaMapper.Map(table, request, mappedAt);

            results.Add(new TvaLineMappingResult
            {
                SourceRegimeCode = line.SourceRegimeCode,
                LineRef = line.LineRef,
                IsMapped = result.IsMapped,
                Category = result.Category?.ToString(),
                Rate = result.Rate,
                Vatex = result.Vatex,
                BlockReason = result.BlockReason,
            });
        }

        return new DocumentTvaMappingResult
        {
            TableExists = true,
            IsValidated = table.IsValidated,
            MappingVersion = table.MappingVersion,
            Lines = results,
        };
    }

    private static List<TvaLineMappingResult> BlockAllForMissingTable(IReadOnlyList<TvaLineMappingRequest> lines)
    {
        const string reason =
            "Aucune table de mapping TVA n'est définie pour ce tenant : document bloqué (aucune catégorie " +
            "n'est devinée). Action opérateur : créez la table dans la console (Paramétrage › TVA), puis " +
            "faites-la valider par l'expert-comptable avant tout envoi.";

        var blocked = new List<TvaLineMappingResult>(lines.Count);
        foreach (var line in lines)
        {
            blocked.Add(new TvaLineMappingResult
            {
                SourceRegimeCode = line.SourceRegimeCode,
                LineRef = line.LineRef,
                IsMapped = false,
                BlockReason = reason,
            });
        }

        return blocked;
    }

    private static MappingPart ToDomainPart(TvaMappingPart part) => part switch
    {
        TvaMappingPart.Adjudication => MappingPart.Adjudication,
        TvaMappingPart.Frais => MappingPart.Frais,
        TvaMappingPart.Autre => MappingPart.Autre,
        _ => throw new ArgumentOutOfRangeException(nameof(part), part, "Part de mapping TVA inconnue."),
    };
}
