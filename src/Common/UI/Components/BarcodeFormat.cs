namespace Stratum.Common.UI.Components;

/// <summary>Barcode format for the <see cref="Barcode"/> component.</summary>
public enum BarcodeFormat
{
    /// <summary>Code 128 — high-density alphanumeric (default).</summary>
    Code128,

    /// <summary>EAN-13 — 12 or 13 numeric digits (retail).</summary>
    Ean13,

    /// <summary>EAN-8 — 7 or 8 numeric digits (small packaging).</summary>
    Ean8,

    /// <summary>UPC-A — 11 or 12 numeric digits (North American retail).</summary>
    UpcA,

    /// <summary>Code 39 — uppercase alphanumeric + special characters.</summary>
    Code39,

    /// <summary>ITF-14 — 14 numeric digits (shipping/logistics).</summary>
    Itf14,
}
