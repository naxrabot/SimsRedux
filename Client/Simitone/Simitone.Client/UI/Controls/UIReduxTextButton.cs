using FSO.Client;
using FSO.Client.UI.Framework;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.Utils;
using System;
using System.Collections.Generic;

namespace Simitone.Client.UI.Controls
{
    /// <summary>
    /// SimsRedux: a Redux-look text button that behaves exactly like a Sims 1 art button
    /// (UITS1Button: Disabled, Selected, OnButtonClick...), so a screen built from Sims 1 buttons can
    /// swap in Redux ones without changing anything else.
    /// </summary>
    public class UIReduxTextButton : UITS1Button
    {
        public string Caption;
        public int TextSize = 13;
        public readonly int W, H;
        private TextStyle Style;

        private static Dictionary<long, Texture2D> Blanks = new Dictionary<long, Texture2D>();

        /// <summary>An empty sheet of the right size: it gives the button its clickable area.</summary>
        private static Texture2D Blank(int w, int h)
        {
            long key = ((long)w << 32) | (uint)h;
            Texture2D tex;
            if (Blanks.TryGetValue(key, out tex) && tex != null && !tex.IsDisposed) return tex;
            tex = new Texture2D(GameFacade.GraphicsDevice, Math.Max(1, w * 4), Math.Max(1, h));
            tex.SetData(new Color[tex.Width * tex.Height]);
            Blanks[key] = tex;
            return tex;
        }

        public UIReduxTextButton(string caption, int width, int height) : base(Blank(width, height), 4)
        {
            Caption = caption;
            W = width;
            H = height;
            Style = TextStyle.DefaultLabel.Clone();
            Style.Color = Color.White;
            Style.Shadow = true;
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            // UIButton's frames: 0 = normal, 1 = pressed, 2 = hover
            bool pressed = CurrentFrame == 1, hover = CurrentFrame == 2;
            var b = ReduxTheme.Base;
            Color top, bottom;
            if (Disabled) { top = ReduxTheme.Deep; bottom = ReduxTheme.Deeper; }
            else if (pressed) { top = ReduxTheme.Deep; bottom = b; }
            else if (Selected || hover) { top = ReduxTheme.Gloss; bottom = ReduxTheme.Hi; }
            else { top = ReduxTheme.Hi; bottom = ReduxTheme.Deep; }
            var border = (hover || Selected) && !Disabled ? ReduxTheme.Glow : ReduxTheme.Rim;
            int r = Math.Min(H / 2, 14);
            DrawLocalTexture(batch, ReduxTheme.Box(W, H, r, Color.Black * 0.35f, Color.Black * 0.35f, Color.Transparent, 0), new Vector2(1, 2));
            DrawLocalTexture(batch, ReduxTheme.Box(W, H, r, top, bottom, border, Selected && !Disabled ? 2 : 1), Vector2.Zero);
            DrawLocalTexture(batch, ReduxTheme.Box(Math.Max(2, W - 8), Math.Max(2, H / 2 - 2), Math.Max(1, r / 2), Color.White * 0.28f, Color.White * 0.05f, Color.Transparent, 0), new Vector2(4, 2));
            Style.Size = TextSize;
            try { while (Style.Size > 6 && Style.MeasureString(Caption ?? "").X > W - 8) Style.Size--; } catch { }
            Style.Color = Disabled ? new Color(160, 166, 195) : Color.White;
            DrawLocalString(batch, Caption ?? "", Vector2.Zero, Style, new Rectangle(0, 0, W, H), TextAlignment.Center | TextAlignment.Middle);
        }
    }
}
