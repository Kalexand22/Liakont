namespace Liakont.Modules.Pipeline.Application;

/// <summary>
/// Marqueur d'assembly de la couche Application du module Pipeline : ancre le scan MediatR
/// (<c>PipelineModuleRegistration</c>). Les handlers (CHECK/SEND/SYNC) arrivent avec PIP01b+ ;
/// l'ancrage est posé dès maintenant pour rester homogène avec les autres modules.
/// </summary>
public interface IPipelineApplicationMarker
{
}
