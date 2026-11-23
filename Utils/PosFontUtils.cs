using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace OdooPosLogin;

/// <summary>
/// Resolves the embedded Arabic font (Assets/SFProAR_semibold.ttf) for use across
/// the UI and the thermal receipt renderer.
///
/// IMPORTANT: the font is embedded as a WPF resource, so it ships inside the exe and
/// needs no install into C:\Windows\Fonts.
///
/// The only reliable way to bind an embedded font in WPF is to take the FontFamily
/// instance returned by Fonts.GetFontFamilies(...). Rebuilding a FontFamily from a
/// pack:// string (e.g. new FontFamily(packBase, "./Assets/#Family, Fallback"))
/// silently resolves to the system fallback instead of the embedded file, so it is
/// deliberately not used here.
///
/// The font contains Arabic only (no Latin letters or Latin digits). WPF falls back
/// per glyph, so Latin text and prices still render via Segoe UI automatically.
/// </summary>
public static class PosFontUtils
{
    private const string EmbeddedFontPath = "Assets/SFProAR_semibold.ttf";
    private const string LatinFallbackFamily = "Segoe UI Variable Text, Segoe UI";

    private static FontFamily? _arabicFont;

    /// <summary>
    /// The embedded SF Pro AR family, or a Segoe UI fallback if the resource is missing.
    /// Resolve this on the UI thread (App.OnStartup does, before any window loads).
    /// </summary>
    public static FontFamily ArabicFont => _arabicFont ??= LoadEmbeddedFont();

    private static FontFamily LoadEmbeddedFont()
    {
        foreach (var uri in CandidateUris())
        {
            try
            {
                var families = Fonts.GetFontFamilies(uri);
                var embedded = families.FirstOrDefault();
                if (embedded == null) continue;

                // Confirm the resolved face really is our file and really has Arabic.
                var typeface = new Typeface(embedded, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                if (!typeface.TryGetGlyphTypeface(out var glyphTypeface)) continue;
                if (!glyphTypeface.CharacterToGlyphMap.ContainsKey(0x0627)) continue; // alef

                Debug.WriteLine($"[PosFontUtils] Embedded font resolved from {uri}: {embedded.Source}");
                return embedded;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PosFontUtils] {uri} failed: {ex.Message}");
            }
        }

        Debug.WriteLine("[PosFontUtils] Embedded font unavailable; falling back to system font.");
        return new FontFamily(LatinFallbackFamily);
    }

    /// <summary>
    /// The assembly-qualified form is required: a plain "pack://application:,,,/Assets/..."
    /// URI resolves against Application.ResourceAssembly, which is not this assembly in
    /// every host, and silently yields no families. The relative form is kept as a
    /// second attempt for the normal case where this assembly does own the resources.
    /// </summary>
    private static IEnumerable<Uri> CandidateUris()
    {
        string assemblyName = typeof(PosFontUtils).Assembly.GetName().Name ?? "OdooPOS";

        yield return new Uri(
            $"pack://application:,,,/{assemblyName};component/{EmbeddedFontPath}",
            UriKind.Absolute);

        yield return new Uri($"pack://application:,,,/{EmbeddedFontPath}", UriKind.Absolute);
    }

    /// <summary>
    /// Replaces the app-level font resources so every window and control picks up the
    /// embedded Arabic font. Call from App.OnStartup before the main window is created.
    /// </summary>
    public static void ApplyApplicationFonts(ResourceDictionary? resources)
    {
        if (resources == null) return;

        var font = ArabicFont;
        resources["CairoFontFamily"] = font;
        resources["DisplayFontFamily"] = font;
    }
}

