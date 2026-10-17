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

        // SimsRedux: the original Sims 1 picture for this button, drawn on the Redux button
        private Texture2D Art;
        private int ArtFrames = 4;

        /// <summary>
        /// Shows a Sims 1 button picture on the button (its frames: normal, hover, pressed, disabled...).
        /// A wide button shows the picture with its caption beside it, a small one just the picture.
        /// </summary>
        public void SetArt(Texture2D tex, int frames)
        {
            frames = Math.Max(1, frames);
            if (tex == null || tex.Width < frames * 2 || tex.Height < 2) return; // (missing art: keep the words)
            Art = tex;
            ArtFrames = frames;
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
            var textArea = new Rectangle(0, 0, W, H);
            if (Art != null)
            {
                int fw = Art.Width / ArtFrames, fh = Art.Height;
                int frame;
                if (Disabled) frame = ArtFrames >= 6 ? 4 : 3;
                else if (Selected && !pressed) frame = SelectedFrame;
                else if (pressed) frame = 2;
                else if (hover) frame = 1;
                else frame = 0;
                frame = Math.Max(0, Math.Min(ArtFrames - 1, frame));
                bool withText = !string.IsNullOrEmpty(Caption) && Caption.Length > 1 && W >= H * 2.2f;
                float bw = withText ? H - 6 : W - 6, bh = H - 6;
                float k = Math.Min(bw / fw, bh / fh);
                var size = new Vector2(fw * k, fh * k);
                var pos = withText ? new Vector2(4 + (bw - size.X) / 2, 3 + (bh - size.Y) / 2)
                                   : new Vector2((W - size.X) / 2, (H - size.Y) / 2);
                DrawLocalTexture(batch, Art, new Rectangle(frame * fw, 0, fw, fh), pos, new Vector2(k, k));
                if (!withText) return;
                textArea = new Rectangle(H - 2, 0, W - H - 2, H);
            }
            Style.Size = TextSize;
            try { while (Style.Size > 6 && Style.MeasureString(Caption ?? "").X > textArea.Width - 8) Style.Size--; } catch { }
            Style.Color = Disabled ? new Color(160, 166, 195) : Color.White;
            DrawLocalString(batch, Caption ?? "", new Vector2(textArea.X, 0), Style, new Rectangle(0, 0, textArea.Width, H), TextAlignment.Center | TextAlignment.Middle);
        }
    }
}
