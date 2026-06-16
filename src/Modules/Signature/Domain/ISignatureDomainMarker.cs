namespace Liakont.Modules.Signature.Domain;

/// <summary>
/// Marqueur d'assembly de la couche Domain du module Signature. SIG03 (abstraction à capacités) ne porte
/// aucune entité de domaine : la machine de validation du document et son agrégat vivent dans le module
/// générique DocumentApproval (ADR-0028, SIG04), et le hash de binding sur place est une primitive du
/// plug-in Wacom (ADR-0030, SIG08). Le marqueur est livré dès maintenant pour rester homogène avec les
/// autres modules (ossature Stratum 5 couches) quand des invariants de domaine propres à la signature
/// apparaîtront.
/// </summary>
public interface ISignatureDomainMarker;
