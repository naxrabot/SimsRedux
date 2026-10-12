using FSO.Client.UI.Framework;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// SimsRedux: the original Sims 1 interface font ("Variable Sans"), read straight from the
    /// player's own UIGraphics.far (Fonts/variablesans_NN.ffn) and turned into normal game fonts.
    ///
    /// The .ffn format (worked out from the files):
    ///   0x00  "FNTF", file size (u32), ?, glyph count (u16, 224 = characters 32-255), ...
    ///   0x1C  offset of the picture data (u32)
    ///   0x20  224 glyph records, 11 bytes each:
    ///           char (u16), width (u8), height (u8), x (u16), y (u16) in the picture,
    ///           space before (s8), space after (s8), drop from the top of the line (s8)
    ///   then 16 bytes, then the picture: 256 pixels wide, 4 bits per pixel (high nibble first),
    ///   each pixel is how solid the letter is there (0-15).
    /// </summary>
    public static class ReduxFonts
    {
        private static readonly int[] Sizes = { 7, 8, 9, 10, 11, 12, 14, 16, 18, 20 };

        /// <summary>Adds every Sims 1 font size it can read to the given font. True if any worked.</summary>
        public static bool TryLoad(GraphicsDevice gd, Font into)
        {
            int loaded = 0;
            foreach (var size in Sizes)
            {
                try
                {
                    var bytes = TS1UIGraphics.GetBytes("Fonts/variablesans_" + size.ToString("00") + ".ffn");
                    if (bytes == null) continue;
                    var font = Read(gd, bytes);
                    if (font == null) continue;
                    into.AddSize(size, font);
                    loaded++;
                }
                catch (Exception e)
                {
                    Console.WriteLine("[SimsRedux] Font " + size + ": " + e.Message);
                }
            }
            Console.WriteLine("[SimsRedux] Loaded " + loaded + " Sims 1 font sizes.");
            return loaded > 0;
        }

        private static SpriteFont Read(GraphicsDevice gd, byte[] d)
        {
            if (d.Length < 0x40 || d[0] != 'F' || d[1] != 'N' || d[2] != 'T' || d[3] != 'F') return null;
            int count = BitConverter.ToUInt16(d, 0x0A);
            int dataOffset = BitConverter.ToInt32(d, 0x1C);
            if (count <= 0 || count > 1024 || dataOffset <= 0 || dataOffset >= d.Length) return null;

            int pixStart = dataOffset + 16;
            const int width = 256, stride = 128;
            int height = (d.Length - pixStart) / stride;
            if (height <= 0) return null;

            // the picture: white letters, solidness as alpha (premultiplied)
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int b = d[pixStart + y * stride + x / 2];
                    int v = ((x & 1) == 0) ? (b >> 4) : (b & 15);
                    byte a = (byte)(v * 17);
                    pixels[y * width + x] = new Color(a, a, a, a);
                }
            var tex = new Texture2D(gd, width, height);
            tex.SetData(pixels);

            var bounds = new List<Rectangle>();
            var cropping = new List<Rectangle>();
            var chars = new List<char>();
            var kerning = new List<Vector3>();
            int lineHeight = 0;
            int spaceAdvance = 0;
            var seen = new HashSet<char>();

            // line height from the normal letters only (some special characters, like 160, are big
            // pictures that would make every line far too tall and push all text upwards)
            for (int i = 0; i < count; i++)
            {
                int o = 0x20 + i * 11;
                if (o + 11 > dataOffset) break;
                int c = BitConverter.ToUInt16(d, o);
                if (c < 0x21 || c > 0x7E) continue;
                int h = d[o + 3], drop = (sbyte)d[o + 10];
                lineHeight = Math.Max(lineHeight, Math.Max(0, drop) + h);
            }
            if (lineHeight <= 0) return null;

            for (int i = 0; i < count; i++)
            {
                int o = 0x20 + i * 11;
                if (o + 11 > dataOffset) break;
                char c = (char)BitConverter.ToUInt16(d, o);
                int w = d[o + 2], h = d[o + 3];
                int x = BitConverter.ToUInt16(d, o + 4), y = BitConverter.ToUInt16(d, o + 6);
                int before = (sbyte)d[o + 8], after = (sbyte)d[o + 9], drop = (sbyte)d[o + 10];
                if (seen.Contains(c)) continue;
                seen.Add(c);

                if (c == ' ' || c == (char)160)
                {
                    // spaces hold no real spacing (160 is even a picture): use about a quarter line
                    spaceAdvance = Math.Max(2, (int)Math.Round(lineHeight * 0.22f));
                    bounds.Add(new Rectangle(0, 0, 0, 0));
                    cropping.Add(new Rectangle(0, 0, 0, 0));
                    kerning.Add(new Vector3(0, spaceAdvance, 0));
                    chars.Add(c);
                    continue;
                }
                if (w == 0 || h == 0 || x + w > width || y + h > height) continue;
                if (h > lineHeight * 1.6f) continue; // oversized pictures, not letters
                bounds.Add(new Rectangle(x, y, w, h));
                // drop can be negative (accents above capitals reach above the line)
                cropping.Add(new Rectangle(0, drop, w, h));
                kerning.Add(new Vector3(before, w, after));
                chars.Add(c);
            }
            if (chars.Count == 0) return null;
            // every character's box is a full line tall, so lines measure the same height
            for (int i = 0; i < cropping.Count; i++)
                cropping[i] = new Rectangle(cropping[i].X, cropping[i].Y, cropping[i].Width, lineHeight);

            // SpriteFont needs the characters in order
            var order = new List<int>();
            for (int i = 0; i < chars.Count; i++) order.Add(i);
            order.Sort((a, b) => chars[a].CompareTo(chars[b]));
            var sb = new List<Rectangle>(); var sc = new List<Rectangle>(); var sch = new List<char>(); var sk = new List<Vector3>();
            foreach (var i in order) { sb.Add(bounds[i]); sc.Add(cropping[i]); sch.Add(chars[i]); sk.Add(kerning[i]); }

            char? fallback = sch.Contains('*') ? '*' : (char?)null;
            return new SpriteFont(tex, sb, sc, sch, lineHeight + 2, 0, sk, fallback);
        }
    }
}
