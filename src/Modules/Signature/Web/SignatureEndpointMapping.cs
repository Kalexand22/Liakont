namespace Liakont.Modules.Signature.Web;

using System;
using Liakont.Modules.Signature.Application.OnSite;
using Liakont.Modules.Signature.Contracts.OnSite;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Stratum.Common.Abstractions.Security;

/// <summary>
/// Endpoints du module Signature, montés sous <c>/api/v1/signature</c> par le Host. SIG08 expose le proxy
/// SUR PLACE (ADR-0030 §3/§5 ; F17 §6) : la capture postée par le client soft Wacom, et l'enregistrement d'un
/// signataire VÉRIFIÉ. Aucune logique métier ici (module-rules §1 ; CLAUDE.md review n°19) — l'endpoint valide
/// l'entrée et dispatch une commande MediatR.
/// <para>
/// SÉCURITÉ — tenant-scoping serveur (CLAUDE.md n°9) : le <c>company_id</c> et le <c>user_id</c> proviennent
/// TOUJOURS du principal authentifié (<see cref="IActorContextAccessor"/>), JAMAIS du payload. Le handler
/// re-vérifie l'appartenance <c>document_id → company_id</c> (404 sinon). Le DÉPOSANT (uploader) est le
/// principal authentifié ; le SIGNATAIRE est résolu par la liaison vérifiée séparée, jamais le payload
/// (ADR-0030 §5, test d'usurpation). Le mapping des exceptions (NotFound → 404) est assuré par le Host.
/// </para>
/// </summary>
public static class SignatureEndpointMapping
{
    /// <summary>Permission d'action opérateur (capture sur place par le poste de la salle des ventes).</summary>
    private const string ActionsPermission = "liakont.actions";

    /// <summary>Permission de paramétrage (enregistrement d'un signataire vérifié = acte d'identité).</summary>
    private const string SettingsPermission = "liakont.settings";

    public static IEndpointRouteBuilder MapSignatureEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/signature");

        // POST /api/v1/signature/onsite-capture — capture de signature sur place postée par le client Wacom.
        group.MapPost("/onsite-capture", async (
            OnSiteCaptureRequest? request,
            ISender sender,
            IActorContextAccessor actorAccessor,
            CancellationToken ct) =>
        {
            var validation = ValidateCapture(request);
            if (validation is not null)
            {
                return Results.BadRequest(new ActionProblem(validation));
            }

            var actor = actorAccessor.Current;
            if (actor.CompanyId is not { } companyId)
            {
                return Results.BadRequest(new ActionProblem(
                    "Aucun tenant n'est associé au compte appelant : la capture sur place est tenant-scopée."));
            }

            var result = await sender.Send(
                new OnSiteCaptureCommand
                {
                    CompanyId = companyId,
                    UploaderUserId = actor.UserId,
                    DocumentId = request!.DocumentId,
                    SignedBindingHash = request.SignedBindingHash,
                    EncryptedFssBase64 = request.EncryptedFssBase64,
                    SignatureImagePngBase64 = request.SignatureImagePngBase64,
                    DeclaredOperatorIdentity = request.DeclaredOperatorIdentity,
                    CapturedAtUtc = request.CapturedAtUtc,
                },
                ct);

            return Results.Ok(result);
        }).RequireAuthorization(ActionsPermission);

        // POST /api/v1/signature/documents/{documentId}/verified-signer — enregistre le SIGNATAIRE vérifié
        // (mandant identifié en personne par la SVV), distinct de la capture (ADR-0030 §5). 404 hors tenant.
        group.MapPost("/documents/{documentId:guid}/verified-signer", async (
            Guid documentId,
            RegisterVerifiedSignerRequest? request,
            ISender sender,
            IActorContextAccessor actorAccessor,
            CancellationToken ct) =>
        {
            if (request is null
                || string.IsNullOrWhiteSpace(request.SignerIdentity)
                || string.IsNullOrWhiteSpace(request.VerificationMethod))
            {
                return Results.BadRequest(new ActionProblem(
                    "L'identité du signataire vérifié et la méthode de vérification sont obligatoires."));
            }

            var actor = actorAccessor.Current;
            if (actor.CompanyId is not { } companyId)
            {
                return Results.BadRequest(new ActionProblem(
                    "Aucun tenant n'est associé au compte appelant : l'enregistrement du signataire est tenant-scopé."));
            }

            var bindingId = await sender.Send(
                new RegisterVerifiedSignerCommand
                {
                    CompanyId = companyId,
                    RegisteredByUserId = actor.UserId,
                    DocumentId = documentId,
                    SignerIdentity = request.SignerIdentity,
                    VerificationMethod = request.VerificationMethod,
                },
                ct);

            return Results.Created($"/api/v1/signature/documents/{documentId}/verified-signer", new { bindingId });
        }).RequireAuthorization(SettingsPermission);

        return app;
    }

    /// <summary>Valide la capture à la frontière (champs obligatoires + base64 décodable) → message FR, ou <c>null</c> si valide.</summary>
    private static string? ValidateCapture(OnSiteCaptureRequest? request)
    {
        if (request is null)
        {
            return "Le corps de la requête de capture est obligatoire.";
        }

        if (request.DocumentId == Guid.Empty)
        {
            return "L'identifiant du document est obligatoire.";
        }

        if (string.IsNullOrWhiteSpace(request.SignedBindingHash))
        {
            return "L'empreinte de binding signée est obligatoire.";
        }

        if (!IsBase64(request.EncryptedFssBase64))
        {
            return "La forme de stockage de la signature (FSS) doit être un contenu Base64 valide.";
        }

        if (!IsBase64(request.SignatureImagePngBase64))
        {
            return "Le rendu PNG de la signature doit être un contenu Base64 valide.";
        }

        return null;
    }

    private static bool IsBase64(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        Span<byte> buffer = new byte[((value.Length * 3) / 4) + 4];
        return Convert.TryFromBase64String(value, buffer, out _);
    }

    /// <summary>Détail d'erreur d'action (message opérateur en français — CLAUDE.md n°12).</summary>
    public sealed record ActionProblem(string Message);
}
