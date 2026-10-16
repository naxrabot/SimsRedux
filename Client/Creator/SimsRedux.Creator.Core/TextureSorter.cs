using System;
using System.Collections.Generic;
using System.Linq;

namespace SimsRedux.Creator;

/// <summary>What a Sims 3 texture is, as far as the Creator can tell.</summary>
public enum TextureKind
{
    /// <summary>A real, coloured picture (wood, tiles, carpet, a painting...) - worth making into something.</summary>
    Usable,
    /// <summary>Its name says it's a helper map (specular, normal, mask, overlay...).</summary>
    HelperByName,
    /// <summary>Its name says it's an effect, the sky, water, UI and so on.</summary>
    EffectByName,
    /// <summary>A colouring mask: a few pure colours (red/green/blue/yellow) or black and white only.</summary>
    Mask,
    /// <summary>A bluish normal map (bumps for lighting).</summary>
    NormalMap,
    /// <summary>Empty, one flat colour, or mostly see-through.</summary>
    Blank,
}

/// <summary>
/// SimsRedux: sorts the useful Sims 3 textures (a wood floor, a tiled wall, a painting) from everything
/// else in the packages - the lighting helper maps, the colouring masks Sims 3 paints its patterns
/// through, normal maps, effects and UI pictures. First by name (free), then by looking at the picture.
/// </summary>
public static class TextureSorter
{
    // name pieces (split on '_', ' ', '-' and '.') that mark a helper map: tileArt01_s, Floor_Puzzle_m, stone_4 ...
    private static readonly HashSet<string> HelperTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "s", "n", "m", "ma", "mk", "msk", "mask", "masks", "spec", "specular", "norm", "normal", "normals", "nm", "nrm",
        "bump", "bmp", "alpha", "a", "o", "ov", "ovl", "overlay", "ao", "ambient", "occlusion", "h", "height", "hm",
        "env", "envmap", "refl", "reflection", "gloss", "glossmap", "shadow", "shadowmap", "lightmap", "lm",
        "1", "2", "3", "4", "ch1", "ch2", "ch3", "ch4", "multiplier", "mult", "detail", "dmap", "dm",
    };

    // anywhere in the name: effects, sky, water, UI and the like
    private static readonly string[] EffectWords =
    {
        "screengrab", "caustic", "kaleidoscope", "effect", "filter", "glow", "flare", "particle", "sparkle", "smoke",
        "lightning", "cloud", "skybox", "sky_", "_sky", "water", "ripple", "wave", "noise", "gradient", "ramp", "lut",
        "lightmap", "shadow", "fog", "mist", "ui_", "_ui", "icon", "thumb", "cursor", "font", "loading", "logo", "button",
        "hud", "reflection", "cubemap", "envmap", "falloff", "specular", "normalmap", "bumpmap", "dropshadow", "highlight",
        "selection", "placement", "debug", "dummy", "default", "missing", "placeholder", "test", "fx_", "_fx",
    };

    /// <summary>The sort decided by the name alone (Usable = the name gives no reason to drop it).</summary>
    public static TextureKind ByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return TextureKind.Usable;
        var lower = name.ToLowerInvariant();
        // ignore the "_0x1234ABCD..." instance tail the Creator adds to unnamed or duplicate names
        int hex = lower.IndexOf("_0x", StringComparison.Ordinal);
        if (hex > 0) lower = lower.Substring(0, hex);
        foreach (var w in EffectWords) if (lower.Contains(w)) return TextureKind.EffectByName;
        foreach (var token in lower.Split(new[] { '_', ' ', '-', '.' }, StringSplitOptions.RemoveEmptyEntries))
            if (HelperTokens.Contains(token)) return TextureKind.HelperByName;
        return TextureKind.Usable;
    }

    /// <summary>The sort decided by looking at the picture (scaled down to 64x64 first).</summary>
    public static TextureKind ByLook(Image32 img)
    {
        if (img == null || img.Width < 2 || img.Height < 2) return TextureKind.Blank;
        var small = (img.Width > 64 || img.Height > 64) ? img.Resize(64, 64) : img;
        int n = small.Width * small.Height;
        int seeThrough = 0, saturatedPrimary = 0, blackOrWhite = 0, bluish = 0;
        double sumL = 0, sumL2 = 0, sumR = 0, sumG = 0, sumB = 0;
        var buckets = new HashSet<int>();
        for (int i = 0; i < n; i++)
        {
            int r = small.Rgba[i * 4], g = small.Rgba[i * 4 + 1], b = small.Rgba[i * 4 + 2], a = small.Rgba[i * 4 + 3];
            if (a < 128) { seeThrough++; continue; }
            int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double l = 0.299 * r + 0.587 * g + 0.114 * b;
            sumL += l; sumL2 += l * l; sumR += r; sumG += g; sumB += b;
            if (max >= 190 && min <= 60 && (max - min) >= 160) saturatedPrimary++;    // pure red, green, blue, yellow...
            if (max <= 40 || min >= 225) blackOrWhite++;
            if (b > 170 && b > r + 30 && b > g + 30 && r > 60 && g > 60) bluish++; // the normal-map lilac-blue
            buckets.Add(((r >> 5) << 6) | ((g >> 5) << 3) | (b >> 5));
        }
        int solid = n - seeThrough;
        if (solid < n * 0.7) return TextureKind.Blank; // mostly see-through: a decal or effect
        double meanL = sumL / solid, varL = sumL2 / solid - meanL * meanL;
        double mr = sumR / solid, mg = sumG / solid, mb = sumB / solid;
        if (Math.Sqrt(Math.Max(0, varL)) < 3 && buckets.Count <= 3) return TextureKind.Blank; // one flat colour
        if (bluish > solid * 0.6 || (mb > 190 && mr > 90 && mr < 170 && mg > 90 && mg < 170)) return TextureKind.NormalMap;
        // colouring masks: mostly pure primaries (and black), with very few colours in all
        if ((saturatedPrimary + blackOrWhite) > solid * 0.85 && saturatedPrimary > solid * 0.1 && buckets.Count <= 12) return TextureKind.Mask;
        // black-and-white only (grout grids, stencils)
        if (blackOrWhite > solid * 0.92 && buckets.Count <= 6) return TextureKind.Mask;
        return TextureKind.Usable;
    }

    /// <summary>Name first, then the picture.</summary>
    public static TextureKind Sort(string name, Image32 img)
    {
        var byName = ByName(name);
        return byName != TextureKind.Usable ? byName : ByLook(img);
    }
}
