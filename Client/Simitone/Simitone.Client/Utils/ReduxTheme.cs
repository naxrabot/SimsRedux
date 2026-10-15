using FSO.Client;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// SimsRedux: the "Redux" interface look - the original Sims 1 layout and icons on smoother
    /// panels in a colour the player picks (Options › Graphics › Interface).
    ///
    /// The player's choices live in redux_settings.ini:
    ///   uiredux = 1 (Redux look, default) or 0 (Classic: the original art untouched)
    ///   uicolor = #rrggbb (default Sims blue, taken from the original wing art)
    ///   uiscale = 0 (fit the screen) or a percentage (100, 125, 150, 200)
    ///
    /// Everything here is made from the player's own game art or drawn in code; nothing is shipped.
    /// </summary>
    public static class ReduxTheme
    {
        public static readonly string[] PresetNames = { "Sims Blue", "Teal", "Garden", "Plum", "Rose", "Graphite" };
        public static readonly Color[] Presets =
        {
            new Color(0x14, 0x58, 0xb0), new Color(0x1b, 0x7f, 0x9a), new Color(0x2f, 0x7d, 0x5b),
            new Color(0x8a, 0x3f, 0x73), new Color(0xa8, 0x43, 0x5a), new Color(0x3a, 0x3f, 0x4c)
        };
        public static readonly int[] ScaleChoices = { 0, 100, 125, 150, 200 };

        /// <summary>Goes up every time the look changes, so cached pictures are remade.</summary>
        public static int Version { get; private set; } = 1;

        private static bool? _Redux;
        public static bool Redux
        {
            get { if (_Redux == null) _Redux = ReduxSettings.GetBool("uiredux", true); return _Redux.Value; }
            set { _Redux = value; ReduxSettings.Set("uiredux", value); Changed(); }
        }

        private static Color? _Base;
        public static Color Base
        {
            get
            {
                if (_Base == null) _Base = Parse(ReduxSettings.GetString("uicolor", "#1458b0"), Presets[0]);
                return _Base.Value;
            }
            set { _Base = value; ReduxSettings.Set("uicolor", ToHex(value)); Changed(); }
        }

        /// <summary>0 = fit the screen, otherwise a percentage.</summary>
        public static int Scale
        {
            get { return Math.Max(0, Math.Min(300, ReduxSettings.GetInt("uiscale", 0))); }
            set { ReduxSettings.Set("uiscale", value); }
        }

        /// <summary>How big the control panel is drawn on a screen this tall.</summary>
        public static float PanelScale(int screenHeight)
        {
            var s = Scale;
            if (s > 0) return s / 100f;
            return Math.Max(1f, (float)Math.Floor(screenHeight / 768f * 4) / 4f);
        }

        public static Color Hi { get { return Shade(Base, 0.25f); } }
        public static Color Gloss { get { return Shade(Base, 0.6f); } }
        public static Color Rim { get { return Shade(Base, 0.45f); } }
        public static Color Deep { get { return Shade(Base, -0.35f); } }
        public static Color Deeper { get { return Shade(Base, -0.6f); } }
        public static Color Edge { get { return Shade(Base, -0.72f); } }
        public static readonly Color Glow = new Color(255, 246, 200);

        private static void Changed()
        {
            Version++;
            foreach (var t in Cache.Values) { try { t.Dispose(); } catch { } }
            Cache.Clear();
        }

        // ------------------------------------------------------------------
        // Colours
        // ------------------------------------------------------------------

        /// <summary>amt above 0 mixes towards white, below 0 towards black.</summary>
        public static Color Shade(Color c, float amt)
        {
            Func<int, int> f = (v) => (int)Math.Max(0, Math.Min(255, Math.Round(amt < 0 ? v * (1 + amt) : v + (255 - v) * amt)));
            return new Color(f(c.R), f(c.G), f(c.B), c.A);
        }

        public static Color Parse(string hex, Color fallback)
        {
            try
            {
                hex = (hex ?? "").Trim().TrimStart('#');
                if (hex.Length != 6) return fallback;
                var n = Convert.ToInt32(hex, 16);
                return new Color((n >> 16) & 255, (n >> 8) & 255, n & 255);
            }
            catch { return fallback; }
        }

        public static string ToHex(Color c)
        {
            return "#" + c.R.ToString("x2") + c.G.ToString("x2") + c.B.ToString("x2");
        }

        private static void ToHsv(Color c, out float h, out float s, out float v)
        {
            float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
            float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            v = max;
            float d = max - min;
            s = max <= 0 ? 0 : d / max;
            if (d <= 0) { h = 0; return; }
            if (max == r) h = ((g - b) / d) % 6f;
            else if (max == g) h = (b - r) / d + 2f;
            else h = (r - g) / d + 4f;
            h /= 6f;
            if (h < 0) h += 1f;
        }

        private static Color FromHsv(float h, float s, float v, byte a)
        {
            h = (h % 1f + 1f) % 1f * 6f;
            int i = (int)Math.Floor(h);
            float f = h - i, p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
            float r, g, b;
            switch (i % 6)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
            return new Color((int)(r * 255), (int)(g * 255), (int)(b * 255), a);
        }

        // ------------------------------------------------------------------
        // Pictures (made once per look, then reused)
        // ------------------------------------------------------------------

        private static Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();
        private static Dictionary<Texture2D, Texture2D> TintCache = new Dictionary<Texture2D, Texture2D>();
        private static int TintVersion;

        /// <summary>
        /// The original art recoloured to the UI colour: coloured parts take the UI colour's hue,
        /// while white, grey, black and green parts (the arc, the LCD screens, the smiley) stay as they are.
        /// </summary>
        public static Texture2D Tinted(Texture2D src)
        {
            if (src == null || src.Width < 2) return src;
            if (TintVersion != Version)
            {
                foreach (var t in TintCache.Values) { try { t.Dispose(); } catch { } }
                TintCache.Clear();
                TintVersion = Version;
            }
            Texture2D result;
            if (TintCache.TryGetValue(src, out result)) return result;
            try
            {
                var data = new Color[src.Width * src.Height];
                src.GetData(data);
                float th, ts, tv;
                ToHsv(Base, out th, out ts, out tv);
                for (int i = 0; i < data.Length; i++)
                {
                    var c = data[i];
                    if (c.A == 0) continue;
                    if (c.G > c.R + 40 && c.G > c.B + 40) continue; // greens (LCD, smiley)
                    float h, s, v;
                    ToHsv(c, out h, out s, out v);
                    if (s < 0.25f) continue;
                    float s2 = ts < 0.75f ? Math.Min(1f, s * (ts / 0.75f)) : s;
                    data[i] = FromHsv(th, s2, v, c.A);
                }
                result = new Texture2D(GameFacade.GraphicsDevice, src.Width, src.Height);
                result.SetData(data);
            }
            catch (Exception e)
            {
                Console.WriteLine("[SimsRedux] Tint failed: " + e.Message);
                result = src;
            }
            TintCache[src] = result;
            return result;
        }

        /// <summary>The bottom bar: a light rim on top, then the UI colour fading darker. 1 pixel wide, stretch it.</summary>
        public static Texture2D BarGradient(int height)
        {
            var key = "bar" + height;
            Texture2D tex;
            if (Cache.TryGetValue(key, out tex)) return tex;
            var data = new Color[height];
            for (int y = 0; y < height; y++)
            {
                Color c;
                if (y < 3) c = Rim;
                else if (y < 5) c = Color.Lerp(Hi, Color.White, 0.2f);
                else
                {
                    float t = (y - 5) / (float)Math.Max(1, height - 6);
                    c = (t < 0.18f) ? Color.Lerp(Hi, Base, t / 0.18f) : Color.Lerp(Base, Deep, (t - 0.18f) / 0.82f);
                }
                data[y] = c;
            }
            tex = new Texture2D(GameFacade.GraphicsDevice, 1, height);
            tex.SetData(data);
            Cache[key] = tex;
            return tex;
        }

        /// <summary>
        /// A rounded box with a top-to-bottom fill and an optional border. Soft edges.
        /// inset = a dark sunken look (a shadow along the top inside).
        /// </summary>
        public static Texture2D Box(int w, int h, int radius, Color top, Color bottom, Color border, int borderW, bool inset = false)
        {
            var key = "box" + w + "x" + h + "r" + radius + top.PackedValue + "_" + bottom.PackedValue + "_" + border.PackedValue + "_" + borderW + (inset ? "i" : "");
            Texture2D tex;
            if (Cache.TryGetValue(key, out tex)) return tex;
            w = Math.Max(1, w); h = Math.Max(1, h);
            var data = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                float t = h <= 1 ? 0 : y / (float)(h - 1);
                var fill = Color.Lerp(top, bottom, t);
                if (inset && y < 3) fill = Color.Lerp(fill, Color.Black, 0.35f * (3 - y) / 3f);
                for (int x = 0; x < w; x++)
                {
                    // distance outside the rounded rectangle (negative = inside)
                    float px = x + 0.5f, py = y + 0.5f;
                    float cx = Math.Max(0, Math.Max(radius - px, px - (w - radius)));
                    float cy = Math.Max(0, Math.Max(radius - py, py - (h - radius)));
                    float dist;
                    if (cx > 0 && cy > 0) dist = (float)Math.Sqrt(cx * cx + cy * cy) - radius; // round corner
                    else dist = -Math.Min(Math.Min(px, w - px), Math.Min(py, h - py));      // straight edge
                    float cover = Math.Max(0, Math.Min(1, 0.5f - dist));
                    if (cover <= 0) { data[x + y * w] = Color.Transparent; continue; }
                    var c = fill;
                    if (borderW > 0 && border.A > 0 && dist > -borderW) c = border;
                    data[x + y * w] = c * cover;
                }
            }
            tex = new Texture2D(GameFacade.GraphicsDevice, w, h);
            tex.SetData(data);
            Cache[key] = tex;
            return tex;
        }
    }
}
