namespace Liakont.Modules.Signature.Application;

/// <summary>
/// Marqueur d'assembly pour l'enregistrement des handlers MediatR de la couche Application du module
/// Signature. SIG03 (abstraction à capacités) n'enregistre aucun handler : le câblage de la règle de gate
/// et des ports par purpose arrive avec SIG06, et l'orchestration des demandes de signature avec
/// SIG07/SIG08. Le marqueur est livré dès maintenant pour rester homogène avec les autres modules.
/// </summary>
public interface ISignatureApplicationMarker;
