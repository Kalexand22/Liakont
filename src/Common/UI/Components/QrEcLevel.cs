namespace Stratum.Common.UI.Components;

/// <summary>QR code error correction level for the <see cref="QRCode"/> component.</summary>
public enum QrEcLevel
{
    /// <summary>~7% error correction capacity.</summary>
    L,

    /// <summary>~15% error correction capacity (default).</summary>
    M,

    /// <summary>~25% error correction capacity.</summary>
    Q,

    /// <summary>~30% error correction capacity.</summary>
    H,
}
