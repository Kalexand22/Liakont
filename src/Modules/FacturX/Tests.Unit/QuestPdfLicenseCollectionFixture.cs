namespace Liakont.Modules.FacturX.Tests.Unit;

using Xunit;

/// <summary>
/// Collection xUnit sérialisant les classes de test qui MUTENT le statique global
/// <c>QuestPDF.Settings.License</c> (FacturXModuleRegistrationTests applique la valeur configurée ;
/// FacturXBuilderTests pose Community avant rendu). Sans cette sérialisation, xUnit exécute les classes
/// en parallèle et une assertion sur la valeur du statique global pourrait être prise en défaut par une
/// écriture concurrente (faux-vert intermittent). La collection garantit qu'elles ne tournent jamais en
/// même temps.
/// </summary>
[CollectionDefinition(Name)]
public sealed class QuestPdfLicenseCollectionFixture
{
    /// <summary>Nom de la collection partagée.</summary>
    public const string Name = "QuestPDF global license (statique partagé)";
}
