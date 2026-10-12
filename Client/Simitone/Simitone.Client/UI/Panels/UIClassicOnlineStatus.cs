using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Common.Utils;
using Microsoft.Xna.Framework;
using Simitone.Client.UI.Screens;
using System;

namespace Simitone.Client.UI.Panels
{
    /// <summary>
    /// SimsRedux online: a small navy tag at the top of the screen while a lot is shared, saying
    /// who's here, plus short notices ("Bob joined your lot!") that fade away.
    /// </summary>
    public class UIClassicOnlineStatus : UIElement
    {
        private TS1GameScreen Game;
        private TextStyle Style, NoticeStyle;
        private string NoticeText;
        private DateTime NoticeTime;

        public UIClassicOnlineStatus(TS1GameScreen game)
        {
            Game = game;
            Style = TextStyle.DefaultLabel.Clone();
            Style.Size = 12;
            Style.Color = new Color(190, 255, 190);
            Style.Shadow = true;
            NoticeStyle = TextStyle.DefaultLabel.Clone();
            NoticeStyle.Size = 14;
            NoticeStyle.Color = new Color(255, 240, 110);
            NoticeStyle.Shadow = true;
        }

        public void Notice(string text)
        {
            NoticeText = text;
            NoticeTime = DateTime.UtcNow;
        }

        private string StatusText()
        {
            if (Game.Host != null)
            {
                var names = Game.Host.GuestNames;
                return names.Count == 0 ? "Online: waiting for friends to join" : "Online: " + string.Join(", ", names) + (names.Count == 1 ? " is here" : " are here");
            }
            if (Game.IsGuest) return "Online: visiting a friend's lot";
            return null;
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            var text = StatusText();
            if (text == null) return;
            var px = TextureGenerator.GetPxWhite(GameFacade.GraphicsDevice);
            float sw = GlobalSettings.Default.GraphicsWidth;

            var w = (int)Style.MeasureString(text).X + 28;
            var pos = new Vector2((int)((sw - w) / 2), 6);
            DrawLocalTexture(batch, px, null, pos + new Vector2(3, 3), new Vector2(w, 22), Color.Black * 0.3f);
            DrawLocalTexture(batch, px, null, pos, new Vector2(w, 22), new Color(0, 0, 82) * 0.9f);
            DrawLocalTexture(batch, px, null, pos + new Vector2(8, 8), new Vector2(6, 6), new Color(90, 230, 90)); // the "online" light
            DrawLocalString(batch, text, pos + new Vector2(18, 0), Style, new Rectangle(0, 0, w - 22, 22), TextAlignment.Left | TextAlignment.Middle);

            if (NoticeText != null)
            {
                var age = (DateTime.UtcNow - NoticeTime).TotalSeconds;
                if (age > 6) { NoticeText = null; return; }
                var fade = (float)Math.Min(1, Math.Max(0, 6 - age));
                var nw = (int)NoticeStyle.MeasureString(NoticeText).X + 30;
                var npos = new Vector2((int)((sw - nw) / 2), 34);
                DrawLocalTexture(batch, px, null, npos, new Vector2(nw, 26), new Color(0, 0, 82) * 0.9f * fade);
                var style = NoticeStyle.Clone();
                style.Color = NoticeStyle.Color * fade;
                DrawLocalString(batch, NoticeText, npos, style, new Rectangle(0, 0, nw, 26), TextAlignment.Center | TextAlignment.Middle);
            }
        }
    }
}
