using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Common.Utils;
using Microsoft.Xna.Framework;
using System;

namespace Simitone.Client.UI.Panels
{
    /// <summary>
    /// SimsRedux: the little navy box that pops up over a house on the neighbourhood map when the
    /// mouse is over it: who lives there (and their money), or the lot's name and price, or
    /// "Community Lot", plus what the current tool (New House / Bulldoze / Rezone) would do.
    ///
    /// It lives inside the map, but the map sets its scale so it is always drawn at the same
    /// size as the rest of the classic interface, and nudges it so it never leaves the window.
    /// </summary>
    public class UIClassicHousePopup : UIElement
    {
        private UINeighborhoodHouseButton House;
        private string Title;
        private string[] Lines = new string[0];
        private string Hint;
        private bool HintOk;
        private Vector2 BoxSize;
        private float ShiftX;
        private bool Below;

        private TextStyle TitleStyle, LineStyle, HintStyle, BadStyle;

        private const int Pad = 10, TitleH = 18, LineH = 15, HintH = 17;

        public UIClassicHousePopup()
        {
            TitleStyle = TextStyle.DefaultLabel.Clone(); TitleStyle.Size = 13; TitleStyle.Color = Color.White; TitleStyle.Shadow = true;
            LineStyle = TextStyle.DefaultLabel.Clone(); LineStyle.Size = 11; LineStyle.Color = new Color(190, 190, 240);
            HintStyle = TextStyle.DefaultLabel.Clone(); HintStyle.Size = 11; HintStyle.Color = new Color(255, 240, 110); HintStyle.Shadow = true;
            BadStyle = TextStyle.DefaultLabel.Clone(); BadStyle.Size = 11; BadStyle.Color = new Color(255, 130, 110); BadStyle.Shadow = true;
            Visible = false;
        }

        /// <summary>Shows the popup for this house (or hides it for null).</summary>
        public void SetHouse(UINeighborhoodHouseButton house, UINeighborhoodSelectionPanel.NeighTool tool)
        {
            House = house;
            Visible = house != null;
            if (house == null) return;

            var info = house.Info;
            Title = info.Title ?? "";
            Lines = info.Lines.ToArray();
            Hint = UIClassicLotInfo.ToolHint(info, tool, out HintOk);

            float w = TitleStyle.MeasureString(Title).X;
            foreach (var line in Lines) w = Math.Max(w, LineStyle.MeasureString(line).X);
            if (Hint != null) w = Math.Max(w, HintStyle.MeasureString(Hint).X);
            float h = Pad + TitleH + Lines.Length * LineH + (Hint != null ? HintH + 2 : 0) + Pad - 2;
            BoxSize = new Vector2((int)Math.Max(120, w + Pad * 2 + 4), (int)h);
        }

        /// <summary>
        /// Keeps the box inside the window. "anchor" is where the popup hangs from, in screen
        /// pixels; "fit" is the interface scale (1 = 800x600).
        /// </summary>
        public void Fit(Vector2 anchor, float fit, float screenW, float screenH)
        {
            if (!Visible) return;
            float left = anchor.X - BoxSize.X * fit / 2, right = left + BoxSize.X * fit;
            ShiftX = 0;
            if (left < 6) ShiftX = (6 - left) / fit;
            else if (right > screenW - 6) ShiftX = (screenW - 6 - right) / fit;
            // too close to the top bar? show it under the house instead
            float top = anchor.Y - (BoxSize.Y + 8) * fit;
            Below = top < 66 * fit;
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible || House == null) return;
            var px = TextureGenerator.GetPxWhite(GameFacade.GraphicsDevice);
            var pos = new Vector2((int)(-BoxSize.X / 2 + ShiftX), Below ? 70 : -BoxSize.Y - 8);

            DrawLocalTexture(batch, px, null, pos + new Vector2(4, 4), BoxSize, Color.Black * 0.35f);
            DrawLocalTexture(batch, px, null, pos, BoxSize, new Color(0, 0, 82) * 0.95f);
            DrawLocalTexture(batch, px, null, pos, new Vector2(BoxSize.X, 2), new Color(120, 120, 200));
            DrawLocalTexture(batch, px, null, pos, new Vector2(2, BoxSize.Y), new Color(120, 120, 200));
            DrawLocalTexture(batch, px, null, pos + new Vector2(0, BoxSize.Y - 2), new Vector2(BoxSize.X, 2), new Color(0, 0, 40));
            DrawLocalTexture(batch, px, null, pos + new Vector2(BoxSize.X - 2, 0), new Vector2(2, BoxSize.Y), new Color(0, 0, 40));

            int w = (int)BoxSize.X - Pad * 2;
            float y = pos.Y + Pad - 2;
            DrawLocalString(batch, Title, new Vector2(pos.X + Pad, y), TitleStyle, new Rectangle(0, 0, w, TitleH), TextAlignment.Center | TextAlignment.Middle);
            y += TitleH;
            foreach (var line in Lines)
            {
                DrawLocalString(batch, line, new Vector2(pos.X + Pad, y), LineStyle, new Rectangle(0, 0, w, LineH), TextAlignment.Center | TextAlignment.Middle);
                y += LineH;
            }
            if (Hint != null)
            {
                y += 2;
                DrawLocalString(batch, Hint, new Vector2(pos.X + Pad, y), HintOk ? HintStyle : BadStyle, new Rectangle(0, 0, w, HintH), TextAlignment.Center | TextAlignment.Middle);
            }
        }
    }
}
