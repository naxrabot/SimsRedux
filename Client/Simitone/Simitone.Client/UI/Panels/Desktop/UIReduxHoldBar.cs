using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.Model;
using Microsoft.Xna.Framework;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Screens;
using Simitone.Client.Utils;

namespace Simitone.Client.UI.Panels.Desktop
{
    /// <summary>
    /// SimsRedux touch (Redux layout): the buttons for the object you're holding in Buy / Build mode -
    /// turn it, put it down, or sell it / put it back. On a PC the mouse does all this (drag to turn,
    /// click to place, Delete to sell); a finger needs buttons.
    /// Drag the object with one finger, tap a spot to put it there, or use Place.
    /// </summary>
    public class UIReduxHoldBar : UIContainer
    {
        public const int BtnW = 116, BtnH = 54, Gap = 8;
        private TS1GameScreen Game;
        private UIReduxButton Left, Right, Place, Sell;

        public UIReduxHoldBar(TS1GameScreen game)
        {
            Game = game;
            Left = Make("Turn Left", 0, () => Turn(-2));
            Right = Make("Turn Right", 1, () => Turn(2));
            Place = Make("Place", 2, () => Game.LotControl?.TouchPlace());
            Sell = Make("Sell", 3, () =>
            {
                var holder = Game.LotControl?.ObjectHolder;
                if (holder?.Holding == null) return;
                if (holder.Holding.IsBought) holder.SellBack(null);
                else holder.Cancel();
            });
            Place.TextSize = 15;
            Visible = false;
        }

        private UIReduxButton Make(string caption, int index, System.Action click)
        {
            var b = new UIReduxButton(caption, BtnW, BtnH);
            b.TextSize = 14;
            b.Position = new Vector2(index * (BtnW + Gap), 0);
            b.OnButtonClick += (e) => click();
            Add(b);
            return b;
        }

        private void Turn(int notches)
        {
            var holder = Game.LotControl?.ObjectHolder;
            if (holder?.Holding == null) return;
            holder.RotateObject(notches);
        }

        public static int Width { get { return BtnW * 4 + Gap * 3; } }

        public override void Update(UpdateState state)
        {
            var lot = Game.LotControl;
            var holder = lot?.ObjectHolder;
            Visible = ReduxTouch.ReduxLayout && lot != null && !lot.LiveMode && holder?.Holding != null;
            if (Visible)
            {
                Sell.Caption = holder.Holding.IsBought ? "Sell" : "Cancel";
                // bottom right, just above the control panel
                var ucp = Game.Frontend?.DesktopUCP as UIClassicUCP;
                float scale = ucp?.UIScale ?? 1f;
                var w = GlobalSettings.Default.GraphicsWidth;
                var h = GlobalSettings.Default.GraphicsHeight;
                Position = new Vector2(w - Width - 14, h - UIClassicUCP.WingH * scale - BtnH - 12);
            }
            if (Visible) base.Update(state);
        }
    }
}
