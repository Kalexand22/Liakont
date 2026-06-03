namespace Stratum.Common.UI.Models;

/// <summary>
/// Context passed to the Content RenderFragment of <see cref="Components.DeclaredFormPage{TEntity}"/>.
/// </summary>
/// <param name="IsCreateMode">True when creating a new entity (Id is null).</param>
/// <param name="IsSaving">True while save is in progress.</param>
/// <param name="IsLoading">True while entity is loading.</param>
/// <param name="Mode">Current form mode (View, Edit, Create).</param>
public sealed record FormPageContext(bool IsCreateMode, bool IsSaving, bool IsLoading, FormMode Mode);
