using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.IO;
using FSO.Common.Rendering.Framework.Model;
using Microsoft.Xna.Framework;
using Simitone.Client.Utils;
using System;
using System.Collections.Generic;

namespace Simitone.Client.UI.Controls
{
    /// <summary>
    /// SimsRedux: notifications in the top-right corner (like The Sims 4). A notice slides in, says
    /// its piece, and fades away after a few seconds - or close it with its X. Moving the mouse onto
    /// one keeps it up. They never pause the game.
    /// </summary>
    public class UIReduxToasts : UIElement
    {
        private class Toast
        {
            public string Title;
            public List<string> Lines;
            public double Age, Life;
            public int Height;
            public float Slide; // 0 = off to the right, 1 = in place
            public bool Closing;
        }

        public const int W = 320, Pad = 10, Gap = 8, Top = 64, MaxShown = 4;

        private static UIReduxToasts Instance;
        private List<Toast> Toasts = new List<Toast>();
        private UIMouseEventRef Mouse;
        private bool MouseOver;
        private TextStyle TitleStyle, BodyStyle, XStyle;

        public static UIReduxToasts Get() { return Instance ?? (Instance = new UIReduxToasts()); }

        /// <summary>Shows a notification. Safe to call from anywhere on the game's thread.</summary>
        public static void Show(string title, string message)
        {
            Get().Push(title, message);
        }

        /// <summary>Keeps the notifications on this screen, above everything else on it.</summary>
        public static void Attach(UIContainer screen)
        {
            var t = Get();
            if (t.Parent != screen)
            {
                t.Parent?.Remove(t);
                screen.Add(t);
            }
            else
            {
                var kids = screen.GetChildren();
                if (kids.Count > 0 && kids[kids.Count - 1] != t) screen.SendToFront(t);
            }
        }

        private UIReduxToasts()
        {
            TitleStyle = TextStyle.DefaultLabel.Clone(); TitleStyle.Size = 12; TitleStyle.Color = Color.White; TitleStyle.Shadow = true;
            BodyStyle = TextStyle.DefaultLabel.Clone(); BodyStyle.Size = 10; BodyStyle.Color = new Color(220, 226, 248);
            XStyle = TextStyle.DefaultLabel.Clone(); XStyle.Size = 11; XStyle.Color = Color.White;
            Mouse = ListenForMouse(new Rectangle(0, 0, 1, 1), OnMouse);
        }

        private void Push(string title, string message)
        {
            title = (title ?? "").Trim();
            message = (message ?? "").Trim();
            if (title.Length == 0 && message.Length == 0) return;
            // the same notice twice in a row (some scripts repeat themselves): once is enough
            foreach (var old in Toasts) if (!old.Closing && old.Title == title && string.Join(" ", old.Lines) == string.Join(" ", Wrap(message))) { old.Age = 0; return; }
            var lines = Wrap(message);
            var t = new Toast
            {
                Title = title.Length > 0 ? title : "Notice",
                Lines = lines,
                Life = Math.Min(16, 6 + message.Length * 0.04),
            };
            t.Height = Pad * 2 + 18 + lines.Count * 14;
            Toasts.Add(t);
            while (Toasts.Count > MaxShown) Toasts.RemoveAt(0);
            try { FSO.HIT.HITVM.Get().PlaySoundEvent(FSO.Client.UI.Model.UISounds.Click); } catch { }
        }

        private List<string> Wrap(string text)
        {
            var lines = new List<string>();
            float width = W - Pad * 2;
            foreach (var para in (text ?? "").Replace("\r", "").Split('\n'))
            {
                var words = para.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var line = "";
                foreach (var w in words)
                {
                    var test = line.Length == 0 ? w : line + " " + w;
                    float size;
                    try { size = BodyStyle.MeasureString(test).X; } catch { size = test.Length * 6; }
                    if (size > width && line.Length > 0) { lines.Add(line); line = w; }
                    else line = test;
                }
                if (line.Length > 0) lines.Add(line);
                if (lines.Count >= 8) { lines[lines.Count - 1] += "..."; break; }
            }
            return lines;
        }

        private void OnMouse(UIMouseEventType type, UpdateState state)
        {
            if (type == UIMouseEventType.MouseOver) MouseOver = true;
            else if (type == UIMouseEventType.MouseOut) MouseOver = false;
            else if (type == UIMouseEventType.MouseDown)
            {
                var p = GlobalPoint(new Vector2(state.MouseState.X, state.MouseState.Y));
                int y = 0;
                foreach (var t in Toasts)
                {
                    if (p.Y >= y && p.Y < y + t.Height)
                    {
                        t.Closing = true; // a click anywhere on a notice (or its X) closes it
                        break;
                    }
                    y += t.Height + Gap;
                }
            }
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            var width = FSO.Client.UI.Framework.UIScreen.Current?.ScreenWidth ?? 1024;
            Position = new Vector2(width - W - 12, Top);
            double dt = 1.0 / Math.Max(1, FSO.Common.FSOEnvironment.RefreshRate);
            for (int i = Toasts.Count - 1; i >= 0; i--)
            {
                var t = Toasts[i];
                if (t.Closing) { t.Slide -= (float)(dt * 5); if (t.Slide <= 0) Toasts.RemoveAt(i); continue; }
                if (t.Slide < 1) t.Slide = Math.Min(1, t.Slide + (float)(dt * 5));
                if (!MouseOver) t.Age += dt;
                if (t.Age >= t.Life) t.Closing = true;
            }
            int h = 0;
            foreach (var t in Toasts) h += t.Height + Gap;
            Mouse.Region = new Rectangle(0, 0, W, Math.Max(1, h));
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible || Toasts.Count == 0) return;
            int y = 0;
            foreach (var t in Toasts)
            {
                float slide = Math.Max(0, Math.Min(1, t.Slide));
                int x = (int)((1 - slide) * (W + 20));
                var fade = Math.Min(1f, (float)Math.Max(0, t.Life - t.Age)); // the last second fades out
                fade = Math.Max(fade, t.Closing ? slide : 0);
                DrawLocalTexture(batch, ReduxTheme.Box(W, t.Height, 10, Color.Black * 0.35f, Color.Black * 0.35f, Color.Transparent, 0), null, new Vector2(x + 2, y + 3), Vector2.One, Color.White * fade);
                DrawLocalTexture(batch, ReduxTheme.Box(W, t.Height, 10, ReduxTheme.Hi, ReduxTheme.Deep, ReduxTheme.Rim, 1), null, new Vector2(x, y), Vector2.One, Color.White * fade);
                TitleStyle.Color = Color.White * fade;
                DrawLocalString(batch, t.Title, new Vector2(x + Pad, y + Pad), TitleStyle, new Rectangle(0, 0, W - Pad * 2 - 20, 18), TextAlignment.Left | TextAlignment.Middle);
                XStyle.Color = Color.White * (0.8f * fade);
                DrawLocalString(batch, "X", new Vector2(x + W - Pad - 14, y + Pad), XStyle, new Rectangle(0, 0, 14, 18), TextAlignment.Center | TextAlignment.Middle);
                BodyStyle.Color = new Color(220, 226, 248) * fade;
                int ly = y + Pad + 18;
                foreach (var line in t.Lines)
                {
                    DrawLocalString(batch, line, new Vector2(x + Pad, ly), BodyStyle, new Rectangle(0, 0, W - Pad * 2, 14), TextAlignment.Left | TextAlignment.Middle);
                    ly += 14;
                }
                y += t.Height + Gap;
            }
        }
    }
}
