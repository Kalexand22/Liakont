namespace Liakont.PaClients.Contract.Tests;

using FluentAssertions;
using Xunit;

/// <summary>
/// Garde des fabriques de documents PARTAGÉES par la suite de contrat (<see cref="ContractTestDocuments"/>).
/// Ce sont les SEULS tests autoportants de ce projet : la suite de contrat abstraite
/// (<see cref="PaClientContractTests"/>), elle, s'exécute dans l'assembly de CHAQUE plug-in (ex.
/// <c>Liakont.PaClients.Fake.Tests.Unit</c>), jamais ici. Si quelqu'un casse une fabrique partagée,
/// c'est ici que ça échoue — pas dans dix suites de plug-ins.
/// </summary>
public sealed class ContractTestDocumentsTests
{
    [Fact]
    public void Invoice_Is_A_Sale_Document_Without_Credit_Note_Reference()
    {
        var invoice = ContractTestDocuments.Invoice("F-1");

        invoice.Number.Should().Be("F-1");
        invoice.SourceDocumentKind.Should().Be("FACTURE");
        invoice.CreditNoteRefs.Should().BeEmpty("une facture de vente n'est pas un avoir");
    }

    [Fact]
    public void CreditNote_Carries_Its_Origin_Invoice_Reference()
    {
        var creditNote = ContractTestDocuments.CreditNote("A-1");

        creditNote.Number.Should().Be("A-1");
        creditNote.SourceDocumentKind.Should().Be("AVOIR");
        creditNote.CreditNoteRefs.Should().ContainSingle(
            "un avoir porte la référence de sa facture d'origine (lien transmis au plug-in)");
    }
}
