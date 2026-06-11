namespace Liakont.Modules.TenantSettings.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Définit (upsert) l'activation du vertical « vente aux enchères » du tenant courant (paramétrage
/// produit, décision opérateur D4, lot FIX03). Journalisée comme toute mutation de paramétrage.
/// </summary>
public record SetAuctionVerticalActivationCommand : ICommand
{
    /// <summary><c>true</c> pour activer le vertical enchères (découpage Adjudication / Frais), <c>false</c> sinon.</summary>
    public required bool Enabled { get; init; }
}
