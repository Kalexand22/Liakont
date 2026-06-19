namespace Liakont.Modules.DocumentApproval.Tests.Unit;

using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.DocumentApproval.Infrastructure;
using Stratum.Common.Infrastructure.Database;
using Xunit;

/// <summary>
/// Validation du niveau de preuve requis (SIG06, V005). <c>SetRequiredLevelAsync</c> parse l'entrée AVANT
/// d'ouvrir une connexion : un niveau invalide lève donc <see cref="ArgumentException"/> sans toucher la base.
/// Le contrat n'accepte que des NOMS applicables (Recorded/SES/AES/QES) — pas une valeur numérique, ni None, ni
/// un ensemble combiné (garde anti-faux-vert : <c>Enum.TryParse("2")</c> rendrait SES en silence).
/// </summary>
public sealed class DocumentApprovalRequirementsTests
{
    [Theory]
    [InlineData("2")] // valeur numérique → SES via TryParse, rejetée par le round-trip
    [InlineData("None")] // le défaut « pas d'exigence » est Recorded, pas None
    [InlineData("0")] // None numérique
    [InlineData("recorded")] // casse divergente
    [InlineData("SES, AES")] // ensemble combiné
    [InlineData("QES2")] // nom inconnu
    [InlineData("")] // vide
    public async Task SetRequiredLevel_Rejects_A_Non_Applicable_Level_Name(string invalid)
    {
        var sut = new PostgresDocumentApprovalRequirements(new ThrowingConnectionFactory());

        var act = () => sut.SetRequiredLevelAsync(Guid.NewGuid(), ValidationPurpose.MandateSignature, invalid);

        // L'ArgumentException survient au parse, AVANT toute ouverture de connexion (la fabrique lèverait sinon).
        await act.Should().ThrowAsync<ArgumentException>(
            "« {0} » n'est pas un nom de niveau applicable (Recorded/SES/AES/QES)", invalid);
    }

    /// <summary>Fabrique de connexion qui échoue si on l'ouvre : prouve que le rejet précède tout accès base.</summary>
    private sealed class ThrowingConnectionFactory : IConnectionFactory
    {
        public Task<IDbConnection> OpenAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Une entrée invalide ne doit JAMAIS ouvrir de connexion (rejet au parse).");
    }
}
