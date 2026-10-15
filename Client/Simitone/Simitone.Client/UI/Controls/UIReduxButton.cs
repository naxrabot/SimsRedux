using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Client.UI.Model;
using FSO.Common.Rendering.Framework.IO;
using FSO.Common.Rendering.Framework.Model;
using FSO.HIT;
using Microsoft.Xna.Framework;
using Simitone.Client.Utils;
using System;

namespace Simitone.Client.UI.Controls
{
    /// <summary>
    /// SimsRedux: a rounded, glossy text button in the UI colour, for the Redux look.
    /// Lighter when the mouse is over it, darker while pressed.
    /// </summary>
    public class UIReduxButton : UIElement
    {
        public event Action<UIElement> OnButtonClick;
        public string Caption;
        public int TextSize = 13;
        public bool Disabled;
        public readonly int Width, Height;
        private bool Hover, Down;
        private TextStyle Style;

        public UIReduxButton(string caption, int width, int height = 24)
        {
            Caption = caption;
            Width = width;
            Height = height;
            Style = TextStyle.DefaultLabel.Clone();
            Style.Color = Color.White;
            Style.Shadow = true;
            ListenForMouse(new Rectangle(0, 0, width, height), (type, state) =>
            {
                if (type == UIMouseEventType.MouseOver) Hover = true;
                else if (type == UIMouseEventType.MouseOut) { Hover = false; Down = false; }
                else if (type == UIMouseEventType.MouseDown) Down = !Disabled;
                else if (type == UIMouseEventType.MouseUp)
                {
                    if (Down && !Disabled)
                    {
                        HITVM.Get().PlaySoundEvent(UISounds.Click);
                        OnButtonClick?.Invoke(this);
                    }
                    Down = false;
                }
            });
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            var b = ReduxTheme.Base;
            Color top, bottom;
            if (Disabled) { top = ReduxTheme.Deep; bottom = ReduxTheme.Deeper; }
            else if (Down) { top = ReduxTheme.Deep; bottom = b; }
            else if (Hover) { top = ReduxTheme.Gloss; bottom = ReduxTheme.Hi; }
            else { top = ReduxTheme.Hi; bottom = ReduxTheme.Deep; }
            var border = Hover && !Disabled ? ReduxTheme.Glow : ReduxTheme.Rim;
            // soft shadow, then the button
            DrawLocalTexture(batch, ReduxTheme.Box(Width, Height, Height / 2, Color.Black * 0.35f, Color.Black * 0.35f, Color.Transparent, 0), new Vector2(1, 2));
            DrawLocalTexture(batch, ReduxTheme.Box(Width, Height, Height / 2, top, bottom, border, 1), Vector2.Zero);
            // glossy shine on the top half
            DrawLocalTexture(batch, ReduxTheme.Box(Width - 8, Height / 2 - 2, Height / 4, Color.White * 0.28f, Color.White * 0.05f, Color.Transparent, 0), new Vector2(4, 2));
            Style.Size = TextSize;
            Style.Color = Disabled ? new Color(170, 175, 200) : Color.White;
            DrawLocalString(batch, Caption ?? "", Vector2.Zero, Style, new Rectangle(0, 0, Width, Height), TextAlignment.Center | TextAlignment.Middle);
        }
    }
}
