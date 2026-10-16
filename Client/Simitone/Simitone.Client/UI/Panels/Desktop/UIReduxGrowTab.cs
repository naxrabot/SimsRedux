using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Client.UI.Model;
using FSO.Common.Rendering.Framework.IO;
using FSO.Common.Rendering.Framework.Model;
using FSO.HIT;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.Utils;
using System;

namespace Simitone.Client.UI.Panels.Desktop
{
    /// <summary>
    /// SimsRedux: the little tab with an arrow on top of the catalog pictures (like The Sims 3 / 4):
    /// up makes the grid taller (up to 5 rows), down puts it back to the bar's 2 rows.
    /// </summary>
    public class UIReduxGrowTab : UIElement
    {
        public const int W = 44, H = 15;
        public bool Up = true;
        public event Action OnClick;
        private bool Hover;
        private static Texture2D ArrowUp, ArrowDown;

        public UIReduxGrowTab()
        {
            ListenForMouse(new Rectangle(0, 0, W, H), (type, state) =>
            {
                if (type == UIMouseEventType.MouseOver) Hover = true;
                else if (type == UIMouseEventType.MouseOut) Hover = false;
                else if (type == UIMouseEventType.MouseUp && Hover)
                {
                    HITVM.Get().PlaySoundEvent(UISounds.Click);
                    OnClick?.Invoke();
                }
            });
        }

        /// <summary>A small white triangle pointing up (drawn flipped for down).</summary>
        private static Texture2D GetArrow(bool up)
        {
            if (up && ArrowUp != null) return ArrowUp;
            if (!up && ArrowDown != null) return ArrowDown;
            int w = 11, h = 6;
            var data = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                int row = up ? y : (h - 1 - y);
                for (int x = 0; x < w; x++)
                    data[y * w + x] = Math.Abs(x - w / 2) <= row ? Color.White : Color.Transparent;
            }
            var tex = new Texture2D(GameFacade.GraphicsDevice, w, h);
            tex.SetData(data);
            if (up) ArrowUp = tex; else ArrowDown = tex;
            return tex;
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            var top = Hover ? ReduxTheme.Gloss : ReduxTheme.Hi;
            DrawLocalTexture(batch, ReduxTheme.Box(W, H + 6, 7, top, ReduxTheme.Deep, Hover ? ReduxTheme.Glow : ReduxTheme.Rim, 1), Vector2.Zero);
            var a = GetArrow(Up);
            DrawLocalTexture(batch, a, new Vector2((W - a.Width) / 2, (H - a.Height) / 2 + 1));
        }
    }
}
