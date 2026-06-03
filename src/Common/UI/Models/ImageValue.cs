namespace Stratum.Common.UI.Models;

/// <summary>
/// Represents an image stored in the <c>ImageField</c> component.
/// </summary>
/// <remarks>
/// <b>Memory warning (Blazor Server)</b>: <see cref="Base64Content"/> is held in circuit state.
/// Keep <c>MaxFileSizeBytes</c> at 2 MB or less to limit heap usage (~1.37× file size in Base64).
/// </remarks>
/// <param name="Base64Content">Image data encoded as Base64.</param>
/// <param name="ContentType">MIME type, e.g. <c>"image/jpeg"</c>, <c>"image/png"</c>.</param>
/// <param name="WidthPx">Original image width in pixels.</param>
/// <param name="HeightPx">Original image height in pixels.</param>
/// <param name="SourceUrl">
/// <c>null</c> when the image was uploaded from a local file;
/// the original URL when loaded via <c>AllowUrlInput</c>.
/// </param>
public sealed record ImageValue(
    string Base64Content,
    string ContentType,
    int WidthPx,
    int HeightPx,
    string? SourceUrl);
