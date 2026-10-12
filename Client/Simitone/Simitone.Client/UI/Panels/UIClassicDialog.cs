using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.UI.Controls;
using Simitone.Client.Utils;
using System;
using System.Collections.Generic;

namespace Simitone.Client.UI.Panels
{
    /// <summary>
    /// SimsRedux: the original Sims 1 popup box (e.g. "Purchase House? / Are you sure you want
    /// to buy this house?" with Yes / No). Built from Shared/Dlg/Gendlg.bmp (box edges) and
    /// cpanel/Buttons/ButtonTileDialog.bmp (stretchable button).
    ///
    /// Use the static helpers (Show / YesNo / Ok). When the classic interface is off, they fall
    /// back to Simitone's normal popup, so callers never need to check.
    /// </summary>
    public class UIClassicDialog : UIContainer
    {
        private static readonly Color TextColor = new Color(205, 205, 240);

        private Texture2D Edge;
        private Vector2 BoxSize;
        private UILabel TitleLabel;
        private UILabel MessageLabel;
        private List<UITS1TileButton> Buttons = new List<UITS1TileButton>();

        private const int Padding = 32;
        private const int ButtonWidth = 190;
        private const int ButtonGap = 100;

        public UIClassicDialog(string title, string message, params Tuple<string, Action>[] buttons)
        {
            Edge = TS1UIGraphics.Get("Shared/Dlg/Gendlg.bmp");
            var buttonTex = TS1UIGraphics.Get("cpanel/Buttons/ButtonTileDialog.bmp");

            // size the box to the message, like the original
            int gap = buttons.Length == 2 ? ButtonGap : 20;
            int totalButtons = buttons.Length * ButtonWidth + Math.Max(0, buttons.Length - 1) * gap;
            int width = Math.Max(460, Math.Min(640, 120 + (message?.Length ?? 0) * 5));
            width = Math.Max(width, totalButtons + Padding * 2);

            TitleLabel = MakeLabel(title ?? "", 18);
            TitleLabel.Position = new Vector2(Padding, 18);
            TitleLabel.Alignment = TextAlignment.Left;
            TitleLabel.Size = new Vector2(width - Padding * 2, 26);

            MessageLabel = MakeLabel(message ?? "", 17);
            MessageLabel.Wrapped = true;
            MessageLabel.Alignment = TextAlignment.Center;
            MessageLabel.Position = new Vector2(Padding, 64);
            MessageLabel.Size = new Vector2(width - Padding * 2, 24);
            int lines = Math.Max(1, MessageLabel.NumLines);
            int messageHeight = lines * 26;
            MessageLabel.Size = new Vector2(width - Padding * 2, messageHeight);

            int buttonsY = 64 + messageHeight + 30;
            int bx = (width - totalButtons) / 2;
            foreach (var b in buttons)
            {
                var btn = new UITS1TileButton(buttonTex, b.Item1, ButtonWidth);
                btn.Position = new Vector2(bx, buttonsY);
                var action = b.Item2;
                btn.OnButtonClick += (e) => { Close(); action?.Invoke(); };
                Add(btn);
                Buttons.Add(btn);
                bx += ButtonWidth + gap;
            }

            BoxSize = new Vector2(width, buttonsY + 33 + 34);
            Recentre();
        }

        private UILabel MakeLabel(string text, int size)
        {
            var label = new UILabel();
            label.Caption = text;
            label.CaptionStyle = label.CaptionStyle.Clone();
            label.CaptionStyle.Size = size;
            label.CaptionStyle.Color = TextColor;
            Add(label);
            return label;
        }

        public void Close()
        {
            UIScreen.RemoveDialog(this);
        }

        private void Recentre()
        {
            Position = new Vector2((int)((GlobalSettings.Default.GraphicsWidth - BoxSize.X) / 2),
                                   (int)((GlobalSettings.Default.GraphicsHeight - BoxSize.Y) / 2));
        }

        public override void Update(UpdateState state)
        {
            Recentre();
            base.Update(state);
        }

        public override Rectangle GetBounds()
        {
            return new Rectangle(0, 0, (int)BoxSize.X, (int)BoxSize.Y);
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            DrawBox(batch, BoxSize);
            base.Draw(batch);
        }

        // colours sampled from the original "Purchase House?" box
        private static readonly Color BoxFill = new Color(0, 0, 82);
        private static readonly Color BevelLight = new Color(120, 120, 200);
        private static readonly Color BevelMid = new Color(40, 40, 140);
        private static readonly Color BevelDark = new Color(0, 0, 40);

        /// <summary>Draws the navy box with a bevelled edge, like the original Sims 1 popups.</summary>
        private void DrawBox(UISpriteBatch batch, Vector2 size)
        {
            var px = TextureGenerator.GetPxWhite(GameFacade.GraphicsDevice);
            float w = size.X, h = size.Y;
            // soft drop shadow
            DrawLocalTexture(batch, px, null, new Vector2(6, 6), size, Color.Black * 0.35f);
            DrawLocalTexture(batch, px, null, Vector2.Zero, size, BoxFill);
            // outer bevel (light top/left, dark bottom/right)
            DrawLocalTexture(batch, px, null, new Vector2(0, 0), new Vector2(w, 2), BevelLight);
            DrawLocalTexture(batch, px, null, new Vector2(0, 0), new Vector2(2, h), BevelLight);
            DrawLocalTexture(batch, px, null, new Vector2(0, h - 2), new Vector2(w, 2), BevelDark);
            DrawLocalTexture(batch, px, null, new Vector2(w - 2, 0), new Vector2(2, h), BevelDark);
            // inner bevel line
            DrawLocalTexture(batch, px, null, new Vector2(2, 2), new Vector2(w - 4, 2), BevelMid);
            DrawLocalTexture(batch, px, null, new Vector2(2, 2), new Vector2(2, h - 4), BevelMid);
        }

        /// <summary>Stretches a small edge tile into a box: corners stay, edges and middle stretch.</summary>
        private void DrawNineSlice(UISpriteBatch batch, Texture2D tex, Vector2 size)
        {
            int c = Math.Max(1, tex.Width / 3);       // corner size
            int m = tex.Width - c * 2;               // middle size in the source
            if (m <= 0) { DrawLocalTexture(batch, tex, null, Vector2.Zero, size / new Vector2(tex.Width, tex.Height)); return; }
            float w = size.X, h = size.Y;
            float sx = (w - c * 2) / m, sy = (h - c * 2) / m;

            // corners
            DrawLocalTexture(batch, tex, new Rectangle(0, 0, c, c), new Vector2(0, 0));
            DrawLocalTexture(batch, tex, new Rectangle(c + m, 0, c, c), new Vector2(w - c, 0));
            DrawLocalTexture(batch, tex, new Rectangle(0, c + m, c, c), new Vector2(0, h - c));
            DrawLocalTexture(batch, tex, new Rectangle(c + m, c + m, c, c), new Vector2(w - c, h - c));
            // edges
            DrawLocalTexture(batch, tex, new Rectangle(c, 0, m, c), new Vector2(c, 0), new Vector2(sx, 1));
            DrawLocalTexture(batch, tex, new Rectangle(c, c + m, m, c), new Vector2(c, h - c), new Vector2(sx, 1));
            DrawLocalTexture(batch, tex, new Rectangle(0, c, c, m), new Vector2(0, c), new Vector2(1, sy));
            DrawLocalTexture(batch, tex, new Rectangle(c + m, c, c, m), new Vector2(w - c, c), new Vector2(1, sy));
            // middle
            DrawLocalTexture(batch, tex, new Rectangle(c, c, m, m), new Vector2(c, c), new Vector2(sx, sy));
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// <summary>Shows a classic popup (or Simitone's popup if the classic interface is off).</summary>
        public static void Show(string title, string message, params Tuple<string, Action>[] buttons)
        {
            if (UIClassicNeighborhoodBar.Enabled)
            {
                UIScreen.GlobalShowDialog(new UIClassicDialog(title, message, buttons), true);
                return;
            }

            UIMobileAlert alert = null;
            var alertButtons = new UIAlertButton[buttons.Length];
            for (int i = 0; i < buttons.Length; i++)
            {
                var action = buttons[i].Item2;
                alertButtons[i] = new UIAlertButton(UIAlertButtonType.OK, (b) => { alert.Close(); action?.Invoke(); }, buttons[i].Item1);
            }
            alert = new UIMobileAlert(new UIAlertOptions() { Title = title, Message = message, Buttons = alertButtons });
            UIScreen.GlobalShowDialog(alert, true);
        }

        public static void YesNo(string title, string message, Action yes, Action no = null)
        {
            Show(title, message, Tuple.Create("Yes", yes), Tuple.Create("No", no));
        }

        public static void Ok(string title, string message, Action ok = null)
        {
            Show(title, message, Tuple.Create("OK", ok));
        }
    }

    /// <summary>
    /// SimsRedux: the stretchable Sims 1 dialog button (ButtonTileDialog.bmp, 4 frames)
    /// with a caption drawn on top.
    /// </summary>
    public class UITS1TileButton : UIButton
    {
        private string Text;
        private int ButtonW;
        private TextStyle Style;

        public UITS1TileButton(Texture2D tex, string text, int width) : base(tex)
        {
            ImageStates = 4;
            Text = text;
            ButtonW = width;
            Style = TextStyle.DefaultLabel.Clone();
            Style.Size = 18;
            Style.Color = new Color(205, 205, 240);
            // the base button sized its clickable area to one 65px frame: widen it to the drawn button
            if (ClickHandler != null)
            {
                ClickHandler.Region.Width = width;
                ClickHandler.Region.Height = tex.Height;
            }
        }

        public override Vector2 Size
        {
            get { return new Vector2(ButtonW, Texture?.Height ?? 33); }
            set { }
        }

        public override Rectangle GetBounds()
        {
            return new Rectangle(0, 0, ButtonW, Texture?.Height ?? 33);
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible || Texture == null) return;
            int frames = 4;
            int fw = Texture.Width / frames, h = Texture.Height;
            var state = CurrentFrame; // 0 normal, 1 pressed, 2 hover
            int frame = Disabled ? 3 : (state == 1 ? 2 : (state == 2 ? 1 : 0));
            int x0 = frame * fw;
            int third = fw / 3;
            int mid = fw - third * 2;
            float stretch = (ButtonW - third * 2) / (float)mid;

            DrawLocalTexture(batch, Texture, new Rectangle(x0, 0, third, h), Vector2.Zero);
            DrawLocalTexture(batch, Texture, new Rectangle(x0 + third, 0, mid, h), new Vector2(third, 0), new Vector2(stretch, 1));
            DrawLocalTexture(batch, Texture, new Rectangle(x0 + third + mid, 0, third, h), new Vector2(ButtonW - third, 0));

            var style = Style;
            if (state == 2 && !Disabled) { style = Style.Clone(); style.Color = Color.Cyan; }
            DrawLocalString(batch, Text, Vector2.Zero, style, new Rectangle(0, 0, ButtonW, h), TextAlignment.Center | TextAlignment.Middle);
        }
    }
}
