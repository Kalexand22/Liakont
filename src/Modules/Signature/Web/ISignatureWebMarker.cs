namespace Liakont.Modules.Signature.Web;

/// <summary>
/// Marqueur d'assembly de la couche Web du module Signature. SIG03 (abstraction) ne livre aucun écran : la
/// console de signature (déclencher une demande, suivre le statut, consulter la preuve) arrive avec SIG10
/// (bUnit/Playwright, logique déléguée aux handlers MediatR — CLAUDE.md review n°19). Le marqueur est livré
/// dès maintenant pour rester homogène avec les autres modules (ossature Stratum 5 couches).
/// </summary>
public interface ISignatureWebMarker;
