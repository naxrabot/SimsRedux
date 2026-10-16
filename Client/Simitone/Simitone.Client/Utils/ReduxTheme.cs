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
            if (ReduxTouch.ReduxLayout && s == 0)
            {
                // touch: as big as fits (the panel is about 1040 wide and the bar shouldn't take more
                // than about a third of the screen), so every button is finger-sized
                var w = FSO.Client.GlobalSettings.Default.GraphicsWidth;
                return Math.Max(0.6f, Math.Min(1.4f, Math.Min(w / 1040f, screenHeight / 470f)));
            }
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
                // (a picture that couldn't be tinted is cached as itself: that's the game's own art, never disposed here)
                foreach (var pair in TintCache) { if (pair.Value != pair.Key) { try { pair.Value.Dispose(); } catch { } } }
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

        // ------------------------------------------------------------------
        // Neighbourhood top bar
        // ------------------------------------------------------------------

        private static Dictionary<Texture2D, Tuple<Texture2D, Vector3>> CircleCache = new Dictionary<Texture2D, Tuple<Texture2D, Vector3>>();

        /// <summary>
        /// Keeps only the round part of a Sims 1 round button (the icon and its white ring), dropping
        /// the piece of bar baked in behind it, so it sits cleanly on the Redux bar.
        /// Returns the new sheet; circle = (centre x, centre y, radius) within one frame.
        /// </summary>
        public static Texture2D CircleCut(Texture2D sheet, int frames, out Vector3 circle)
        {
            circle = Vector3.Zero;
            if (sheet == null || sheet.Width < 4 || frames < 1) return sheet;
            Tuple<Texture2D, Vector3> hit;
            if (CircleCache.TryGetValue(sheet, out hit)) { circle = hit.Item2; return hit.Item1; }
            int w = sheet.Width, h = sheet.Height, fw = w / frames;
            var data = new Color[w * h];
            Texture2D result = sheet;
            try
            {
                sheet.GetData(data);
                // the white ring in the first frame gives the circle
                int x0 = int.MaxValue, x1 = -1, y0 = int.MaxValue, y1 = -1;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < fw; x++)
                    {
                        var c = data[x + y * w];
                        if (c.A < 200 || c.R < 185 || c.G < 185 || c.B < 185) continue;
                        if (Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B)) > 40) continue;
                        x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
                    }
                float cx, cy, r;
                if (x1 - x0 > 8 && y1 - y0 > 8)
                {
                    cx = (x0 + x1) / 2f + 0.5f; cy = (y0 + y1) / 2f + 0.5f;
                    r = Math.Max(x1 - x0, y1 - y0) / 2f + 1.2f;
                }
                else { cx = fw / 2f; cy = h / 2f; r = Math.Min(fw, h) / 2f - 2; }
                for (int f = 0; f < frames; f++)
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < fw; x++)
                        {
                            float d = (float)Math.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                            float cover = Math.Max(0, Math.Min(1, r - d + 0.5f));
                            int i = f * fw + x + y * w;
                            if (cover < 1) data[i] = data[i] * cover;
                        }
                result = new Texture2D(GameFacade.GraphicsDevice, w, h);
                result.SetData(data);
                circle = new Vector3(cx, cy, r);
            }
            catch (Exception e)
            {
                Console.WriteLine("[SimsRedux] Button cut failed: " + e.Message);
            }
            CircleCache[sheet] = Tuple.Create(result, circle);
            return result;
        }

        /// <summary>
        /// The Redux neighbourhood bar: a rounded bar with a round socket under each button, all one
        /// smooth shape with a dark outline, in the UI colour. circles are (x, y, radius) in bar pixels.
        /// </summary>
        public static Texture2D TopBar(int width, int barH, List<Vector3> circles)
        {
            var key = "top" + width + "_" + barH + "_" + circles.Count + "_" + (circles.Count > 0 ? circles[0].ToString() : "");
            Texture2D tex;
            if (Cache.TryGetValue(key, out tex)) return tex;
            int h = barH + 26;
            foreach (var c in circles) h = Math.Max(h, (int)Math.Ceiling(c.Y + c.Z) + 5);
            var data = new Color[width * h];
            var edge = Edge;
            float radius = barH / 2f;
            for (int y = 0; y < h; y++)
            {
                Color fill;
                if (y < 2) fill = Rim;
                else if (y < 4) fill = Color.Lerp(Hi, Color.White, 0.25f);
                else
                {
                    float t = (y - 4) / 56f;
                    fill = (t < 0.2f) ? Color.Lerp(Hi, Base, t / 0.2f) : Color.Lerp(Base, Deep, Math.Min(1, (t - 0.2f) / 0.8f));
                }
                for (int x = 0; x < width; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    // signed distance to the bar (rounded right end, slightly rounded left)
                    float d = RoundRectDist(px, py, width, barH, radius);
                    foreach (var c in circles)
                    {
                        float dc = (float)Math.Sqrt((px - c.X) * (px - c.X) + (py - c.Y) * (py - c.Y)) - c.Z;
                        if (dc < d) d = dc;
                    }
                    if (d > 3.5f) continue;
                    // shadow outside, dark outline at the edge, colour inside
                    Color col;
                    if (d > 1f) { float a = 0.3f * (1 - (d - 1f) / 2.5f); col = Color.Black * a; }
                    else if (d > -0.5f) col = edge * Math.Max(0, Math.Min(1, 1f - (d - 0.5f)));
                    else col = Color.Lerp(edge, fill, Math.Min(1, -d - 0.5f));
                    data[x + y * width] = col;
                }
            }
            tex = new Texture2D(GameFacade.GraphicsDevice, width, h);
            tex.SetData(data);
            Cache[key] = tex;
            return tex;
        }

        private static float RoundRectDist(float px, float py, float w, float h, float r)
        {
            float cx = Math.Max(0, Math.Max(r - px, px - (w - r)));
            float cy = Math.Max(0, Math.Max(r - py, py - (h - r)));
            if (cx > 0 && cy > 0) return (float)Math.Sqrt(cx * cx + cy * cy) - r;
            float inside = -Math.Min(Math.Min(px, w - px), Math.Min(py, h - py));
            float outside = Math.Max(Math.Max(-px, px - w), Math.Max(-py, py - h));
            return Math.Max(inside, outside);
        }

        private static Texture2D _Logo;
        private static bool LogoTried;

        /// <summary>The Sims Redux logo (Content/redux/redux_logo.png), or null if it's missing.</summary>
        public static Texture2D Logo
        {
            get
            {
                if (LogoTried) return _Logo;
                LogoTried = true;
                try
                {
                    var path = System.IO.Path.Combine(FSO.Common.FSOEnvironment.ContentDir, "redux/redux_logo.png");
                    if (!FSO.Common.IO.FSOFile.Exists(path)) { Console.WriteLine("[SimsRedux] Logo missing: " + path); return null; }
                    Texture2D tex;
                    using (var stream = FSO.Common.IO.FSOFile.OpenRead(path))
                        tex = FSO.Common.Utils.TextureLoad.FromStream(GameFacade.GraphicsDevice, stream);
                    // the game draws with pre-multiplied alpha: soft edges stay clean
                    var data = new Color[tex.Width * tex.Height];
                    tex.GetData(data);
                    for (int i = 0; i < data.Length; i++)
                    {
                        var c = data[i];
                        if (c.A < 255) data[i] = new Color(c.R * c.A / 255, c.G * c.A / 255, c.B * c.A / 255, c.A);
                    }
                    tex.SetData(data);
                    _Logo = tex;
                }
                catch (Exception e) { Console.WriteLine("[SimsRedux] Couldn't load the logo: " + e.Message); }
                return _Logo;
            }
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
            var key = "box" + w + "x" + h + "r" + radius + "_" + top.PackedValue + "_" + bottom.PackedValue + "_" + border.PackedValue + "_" + borderW + (inset ? "i" : "");
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
