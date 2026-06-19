namespace Liakont.Modules.Signature.Application.OnSite;

using System;
using Liakont.Modules.Signature.Contracts.OnSite;
using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Commande de traitement d'une capture de signature SUR PLACE par le proxy <c>OnSiteCapture</c>
/// (ADR-0030 §3/§4/§5 ; F17 §6). Construite par l'endpoint à partir du <see cref="OnSiteCaptureRequest"/>
/// du client ET des champs résolus CÔTÉ SERVEUR (jamais depuis le payload) : <see cref="CompanyId"/> (tenant
/// authentifié — tenant-scoping serveur) et <see cref="UploaderUserId"/> (le DÉPOSANT = principal
/// authentifié de l'appel, ADR-0030 §5, jamais le signataire ni le payload).
/// </summary>
public sealed record OnSiteCaptureCommand : ICommand<OnSiteCaptureResult>
{
    /// <summary>Tenant authentifié (résolu du principal, jamais du payload) — re-vérifié contre le document.</summary>
    public required Guid CompanyId { get; init; }

    /// <summary>Le DÉPOSANT : principal authentifié de l'appel proxy (poste / opérateur de la salle). Jamais le signataire.</summary>
    public required Guid UploaderUserId { get; init; }

    /// <summary>Document signé sur place.</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Empreinte de binding signée par le client (SHA-256 hex des octets exacts de l'artefact scellé).</summary>
    public required string SignedBindingHash { get; init; }

    /// <summary>FSS chiffrée côté client (Base64) — rapatriée en WORM, jamais convertie en gabarit biométrique.</summary>
    public required string EncryptedFssBase64 { get; init; }

    /// <summary>Rendu PNG de la signature (Base64) — pièce de preuve WORM.</summary>
    public required string SignatureImagePngBase64 { get; init; }

    /// <summary>Identité opérateur DÉCLARÉE (indicative, non probante) — jamais retenue comme signataire.</summary>
    public string? DeclaredOperatorIdentity { get; init; }

    /// <summary>Horodatage de capture côté client (UTC).</summary>
    public required DateTimeOffset CapturedAtUtc { get; init; }
}
