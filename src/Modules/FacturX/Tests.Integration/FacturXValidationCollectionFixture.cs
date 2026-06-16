namespace Liakont.Modules.FacturX.Tests.Integration;

using Xunit;

/// <summary>
/// Définition de collection xUnit partageant les conteneurs de validation (veraPDF + Mustang) entre les
/// tests de conformité : les images Docker ne sont construites/démarrées qu'une fois (coût amorti).
/// Patron du dépôt (<c>*CollectionFixture</c> + littéral de collection, cf. <c>PaymentsCollectionFixture</c>).
/// </summary>
[CollectionDefinition("FacturXValidation")]
public sealed class FacturXValidationCollectionFixture : ICollectionFixture<FacturXValidationContainers>;
