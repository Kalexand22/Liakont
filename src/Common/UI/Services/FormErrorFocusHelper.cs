namespace Stratum.Common.UI.Services;

using Microsoft.JSInterop;

/// <summary>
/// Shared helper for focusing the first error field after form validation.
/// Avoids duplicating JS interop + error-field mapping in every form page.
/// </summary>
public static class FormErrorFocusHelper
{
    /// <summary>
    /// Focuses the first field that has an error, or falls back to the error summary element.
    /// </summary>
    /// <param name="errors">The form errors instance.</param>
    /// <param name="js">JS runtime for focus calls.</param>
    /// <param name="fieldMappings">
    /// Ordered list of (errorKey, elementId) pairs. The first matching error key gets focused.
    /// </param>
    /// <param name="summaryElementId">
    /// Fallback element id to focus when errors exist but none match a field mapping.
    /// </param>
    public static async Task FocusFirstErrorAsync(
        IFormErrors errors,
        IJSRuntime js,
        IReadOnlyList<(string ErrorKey, string ElementId)> fieldMappings,
        string summaryElementId = "error-summary")
    {
        if (!errors.HasErrors)
        {
            return;
        }

        string? targetId = null;
        foreach (var (errorKey, elementId) in fieldMappings)
        {
            if (errors.GetError(errorKey) is not null)
            {
                targetId = elementId;
                break;
            }
        }

        targetId ??= summaryElementId;

        await Task.Yield();
        try
        {
            await js.InvokeVoidAsync("stratumUI.focusElement", targetId);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException or OperationCanceledException)
        {
            // Swallow JS interop errors during prerender/disconnect
        }
    }
}
