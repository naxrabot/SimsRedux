using System;
using System.Collections.Generic;
using System.Globalization;

namespace SimsRedux.Creator;

/// <summary>
/// SimsRedux: colouring textures in. Sims 3 keeps many floors and walls as grey shading layers that the
/// game colours while playing; the Creator does the same once, when the item is made.
/// </summary>
public static class Colours
{
    /// <summary>A named colour for "Make a set".</summary>
    public sealed record Swatch(string Name, byte R, byte G, byte B)
    {
        public string Hex => ToHex(R, G, B);
    }

    /// <summary>The ready-made colours offered by "Make a set" (homely, Sims-like shades).</summary>
    public static readonly IReadOnlyList<Swatch> SetPresets = new[]
    {
        new Swatch("White", 236, 232, 222),
        new Swatch("Cream", 226, 210, 172),
        new Swatch("Sand", 200, 172, 128),
        new Swatch("Tan", 176, 140, 100),
        new Swatch("Brown", 120, 82, 52),
        new Swatch("Dark Brown", 72, 48, 32),
        new Swatch("Grey", 150, 150, 150),
        new Swatch("Charcoal", 64, 64, 68),
        new Swatch("Brick Red", 150, 60, 44),
        new Swatch("Terracotta", 190, 104, 70),
        new Swatch("Pink", 220, 150, 160),
        new Swatch("Mustard", 200, 164, 60),
        new Swatch("Sage", 140, 160, 120),
        new Swatch("Forest Green", 52, 96, 60),
        new Swatch("Sky Blue", 130, 170, 210),
        new Swatch("Navy", 40, 52, 96),
        new Swatch("Lavender", 160, 140, 190),
        new Swatch("Black", 28, 28, 30),
    };

    /// <summary>
    /// Colours a texture in: its average brightness becomes the picked colour, lighter and darker parts
    /// stay lighter and darker (how Sims 3 colours its grey shading layers). See-through pixels stay so.
    /// </summary>
    public static Image32 Colourise(Image32 src, byte cr, byte cg, byte cb)
    {
        var o = new Image32(src.Width, src.Height);
        double sum = 0; int n = 0;
        for (int i = 0; i < src.Rgba.Length; i += 4)
        {
            if (src.Rgba[i + 3] < 128) continue;
            sum += Luma(src.Rgba, i); n++;
        }
        double mean = n == 0 ? 128 : Math.Max(1, sum / n);
        for (int i = 0; i < src.Rgba.Length; i += 4)
        {
            double k = Luma(src.Rgba, i) / mean;
            o.Rgba[i] = Clamp(cr * k);
            o.Rgba[i + 1] = Clamp(cg * k);
            o.Rgba[i + 2] = Clamp(cb * k);
            o.Rgba[i + 3] = src.Rgba[i + 3];
        }
        return o;
    }

    private static double Luma(byte[] p, int i) => 0.299 * p[i] + 0.587 * p[i + 1] + 0.114 * p[i + 2];
    private static byte Clamp(double v) => (byte)Math.Max(0, Math.Min(255, Math.Round(v)));

    public static string ToHex(byte r, byte g, byte b) => "#" + r.ToString("X2") + g.ToString("X2") + b.ToString("X2");

    /// <summary>Reads "#RRGGBB" (or "RRGGBB"); false for anything else.</summary>
    public static bool TryParseHex(string? text, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        var s = (text ?? "").Trim().TrimStart('#');
        if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        r = (byte)(v >> 16); g = (byte)(v >> 8); b = (byte)v;
        return true;
    }
}
