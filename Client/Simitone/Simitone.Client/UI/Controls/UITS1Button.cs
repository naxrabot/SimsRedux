using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Simitone.Client.UI.Controls
{
    /// <summary>
    /// SimsRedux: a button that uses original The Sims 1 button art.
    /// Sims 1 button sheets are laid out left to right as:
    ///   4 frames: normal, hover, pressed, disabled
    ///   6 frames: normal, hover, pressed, pressed-hover, disabled, disabled-pressed
    /// (The Sims Online sheets use a different order, so the base UIButton draw is replaced.)
    /// </summary>
    public class UITS1Button : UIButton
    {
        public int Frames { get; private set; }
        /// <summary>Some sheets have several rows (e.g. the pause button). Which row to draw.</summary>
        public int Rows = 1;
        public int Row = 0;
        /// <summary>Frame used when Selected. Create-a-Sim uses 2 (white), the control panel uses 1 (cyan).</summary>
        public int SelectedFrame = 2;

        public UITS1Button(Texture2D tex, int frames = 4) : base(tex)
        {
            Frames = frames;
            ImageStates = frames;
        }

        /// <summary>Use one row of a multi-row sheet (also shrinks the clickable area to one row).</summary>
        public void SetRow(int rows, int row)
        {
            Rows = Math.Max(1, rows);
            Row = row;
            if (ClickHandler != null && Texture != null) ClickHandler.Region.Height = Texture.Height / Rows;
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible || Texture == null) return;

            int width = Texture.Width / Frames;
            int height = Texture.Height / Math.Max(1, Rows);

            // UIButton's internal frames: 0 = normal, 1 = pressed, 2 = hover
            var state = CurrentFrame;
            bool pressed = state == 1;
            bool hover = state == 2;

            int frame;
            if (Disabled)
                frame = (Frames >= 6) ? 4 : 3;
            else if (Selected && !pressed)
                frame = SelectedFrame;
            else if (pressed && hover && Frames >= 6)
                frame = 3;
            else if (pressed)
                frame = 2;
            else if (hover)
                frame = 1;
            else
                frame = 0;

            if (frame >= Frames) frame = Frames - 1;
            DrawLocalTexture(batch, Texture, new Rectangle(frame * width, Row * height, width, height), Vector2.Zero);
        }
    }
}
