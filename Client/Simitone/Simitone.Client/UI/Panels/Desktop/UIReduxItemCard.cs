using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Content;
using FSO.Files.Formats.IFF.Chunks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.Utils;
using System;
using System.Collections.Generic;

namespace Simitone.Client.UI.Panels.Desktop
{
    /// <summary>
    /// SimsRedux: the item card that pops up just above the Buy / Build bar when the mouse is over a
    /// catalog picture (or while placing something new): a big picture, the name, the price, the
    /// catalog description and what it does for a Sim (Comfort +4, Room +1...).
    /// It's drawn by the panel it belongs to; it never takes the mouse.
    /// </summary>
    public class UIReduxItemCard
    {
        public const int W = 380, Pad = 8, Pic = 90;

        public bool Shown;
        private Texture2D Icon;
        private string Name = "", Price = "", Description = "";
        private List<Tuple<string, int>> Stats = new List<Tuple<string, int>>();
        private uint ShownGUID;
        private TextStyle NameStyle, PriceStyle, BodyStyle, StatStyle;

        public UIReduxItemCard()
        {
            NameStyle = TextStyle.DefaultLabel.Clone(); NameStyle.Size = 13; NameStyle.Color = Color.White; NameStyle.Shadow = true;
            PriceStyle = TextStyle.DefaultLabel.Clone(); PriceStyle.Size = 12; PriceStyle.Color = new Color(255, 240, 160);
            BodyStyle = TextStyle.DefaultLabel.Clone(); BodyStyle.Size = 10; BodyStyle.Color = new Color(215, 222, 245);
            StatStyle = TextStyle.DefaultLabel.Clone(); StatStyle.Size = 10;
        }

        /// <summary>A Buy / Build object, by its GUID.</summary>
        public void SetObject(uint guid, Texture2D icon)
        {
            Shown = true;
            if (guid == ShownGUID && Icon == icon) return;
            ShownGUID = guid;
            Icon = icon;
            Stats.Clear();
            Name = ""; Price = ""; Description = "";
            try
            {
                var obj = Content.Get().WorldObjects.Get(guid);
                var def = obj?.OBJ;
                if (def == null) return;
                var ctss = obj.Resource.Get<CTSS>(def.CatalogStringsID);
                Name = ctss?.GetString(0) ?? def.ChunkLabel ?? "";
                Description = (ctss != null && ctss.Length > 1) ? (ctss.GetString(1) ?? "") : "";
                Price = def.Price > 0 ? "§" + def.Price.ToString("##,#0") : "Free";
                Add("Hunger", def.RatingHunger); Add("Comfort", def.RatingComfort); Add("Hygiene", def.RatingHygiene);
                Add("Bladder", def.RatingBladder); Add("Energy", def.RatingEnergy); Add("Fun", def.RatingFun);
                Add("Room", def.RatingRoom);
                // skills it builds (the original catalog's "Skills" line)
                var skills = new[] { "Cooking", "Mechanical", "Charisma", "Body", "Logic", "Creativity" };
                var flags = def.RatingSkillFlags;
                for (int i = 0; i < skills.Length; i++) if ((flags & (1 << i)) != 0) Stats.Add(Tuple.Create(skills[i] + " skill", 0));
            }
            catch { }
        }

        /// <summary>A wall, floor or roof (no stats): picture, name, price, description.</summary>
        public void SetPlain(Texture2D icon, string name, int price, string description)
        {
            Shown = true;
            ShownGUID = 0;
            Icon = icon;
            Name = name ?? "";
            Price = price > 0 ? "§" + price.ToString("##,#0") : "";
            Description = description ?? "";
            Stats.Clear();
        }

        public void Hide() { Shown = false; ShownGUID = 0; }

        private void Add(string label, int v)
        {
            if (v != 0) Stats.Add(Tuple.Create(label, v));
        }

        /// <summary>Lines of text that fit the width.</summary>
        private static List<string> Wrap(string text, TextStyle style, float width, int maxLines)
        {
            var lines = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return lines;
            var words = text.Replace("\r", " ").Replace("\n", " ").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var line = "";
            foreach (var w in words)
            {
                var test = line.Length == 0 ? w : line + " " + w;
                float size;
                try { size = style.MeasureString(test).X; } catch { size = test.Length * 6; }
                if (size > width && line.Length > 0)
                {
                    lines.Add(line);
                    if (lines.Count >= maxLines) { lines[lines.Count - 1] += "..."; return lines; }
                    line = w;
                }
                else line = test;
            }
            if (line.Length > 0) lines.Add(line);
            return lines;
        }

        /// <summary>Draws the card with its bottom-left corner at (x, bottom), in the panel's own coordinates.</summary>
        public void Draw(UIElement panel, UISpriteBatch batch, int x, int bottom)
        {
            if (!Shown || string.IsNullOrEmpty(Name)) return;
            float textW = W - Pic - Pad * 3;
            var desc = Wrap(Description, BodyStyle, textW, 4);
            int statRows = (Stats.Count + 2) / 3;
            int textH = 20 + 18 + desc.Count * 14 + (statRows > 0 ? 6 + statRows * 15 : 0);
            int h = Math.Max(Pic + Pad * 2, textH + Pad * 2);
            int y = bottom - h;

            panel.DrawLocalTexture(batch, ReduxTheme.Box(W, h, 10, Color.Black * 0.35f, Color.Black * 0.35f, Color.Transparent, 0), new Vector2(x + 2, y + 3));
            panel.DrawLocalTexture(batch, ReduxTheme.Box(W, h, 10, ReduxTheme.Deep, ReduxTheme.Deeper, ReduxTheme.Rim, 1), new Vector2(x, y));

            // the picture, big, on a light square like the catalog's
            panel.DrawLocalTexture(batch, ReduxTheme.Box(Pic, Pic, 6, new Color(150, 190, 220), new Color(110, 150, 190), ReduxTheme.Rim, 1), new Vector2(x + Pad, y + Pad));
            if (Icon != null && Icon.Width > 1)
            {
                // catalog pictures hold two copies side by side (normal and highlighted): just the first, big
                var src = (Icon.Width >= Icon.Height * 1.5f) ? new Rectangle(0, 0, Icon.Width / 2, Icon.Height) : new Rectangle(0, 0, Icon.Width, Icon.Height);
                float s = Math.Min((Pic - 6) / (float)src.Width, (Pic - 6) / (float)src.Height);
                var size = new Vector2(src.Width * s, src.Height * s);
                panel.DrawLocalTexture(batch, Icon, src, new Vector2(x + Pad + (Pic - size.X) / 2, y + Pad + (Pic - size.Y) / 2), new Vector2(s, s));
            }

            int tx = x + Pad * 2 + Pic, ty = y + Pad;
            panel.DrawLocalString(batch, Name, new Vector2(tx, ty), NameStyle, new Rectangle(0, 0, (int)textW, 20), TextAlignment.Left | TextAlignment.Middle);
            ty += 20;
            if (Price.Length > 0) panel.DrawLocalString(batch, Price, new Vector2(tx, ty), PriceStyle, new Rectangle(0, 0, (int)textW, 18), TextAlignment.Left | TextAlignment.Middle);
            ty += 18;
            foreach (var line in desc)
            {
                panel.DrawLocalString(batch, line, new Vector2(tx, ty), BodyStyle, new Rectangle(0, 0, (int)textW, 14), TextAlignment.Left | TextAlignment.Middle);
                ty += 14;
            }
            if (statRows > 0)
            {
                ty += 6;
                int colW = (int)(textW / 3);
                for (int i = 0; i < Stats.Count; i++)
                {
                    var st = Stats[i];
                    var text = st.Item2 == 0 ? st.Item1 : st.Item1 + " " + (st.Item2 > 0 ? "+" : "") + st.Item2;
                    StatStyle.Color = st.Item2 < 0 ? new Color(255, 140, 140) : new Color(150, 255, 150);
                    panel.DrawLocalString(batch, text, new Vector2(tx + (i % 3) * colW, ty + (i / 3) * 15), StatStyle, new Rectangle(0, 0, colW, 15), TextAlignment.Left | TextAlignment.Middle);
                }
            }
        }
    }
}
