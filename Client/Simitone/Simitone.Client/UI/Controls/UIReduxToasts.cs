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
            /// <summary>SimsRedux: a button on the notice (e.g. "Update"), and what it does.</summary>
            public string Button;
            public Action OnButton;
        }

        private const int ButtonW = 110, ButtonH = 24;

        public const int W = 320, Pad = 10, Gap = 8, Top = 64, TouchTop = 150, MaxShown = 4, MaxLines = 8;

        private static UIReduxToasts Instance;
        private List<Toast> Toasts = new List<Toast>();
        private UIMouseEventRef[] Mice = new UIMouseEventRef[MaxShown + 2];
        private bool MouseOver;
        private TextStyle TitleStyle, BodyStyle, XStyle;

        public static UIReduxToasts Get() { return Instance ?? (Instance = new UIReduxToasts()); }

        /// <summary>Shows a notification. Safe to call from anywhere on the game's thread.</summary>
        public static void Show(string title, string message)
        {
            Get().Push(title, message);
        }

        /// <summary>
        /// A notification with a button (e.g. "Update"). It stays until it's answered or closed with
        /// its X - unlike the others, it doesn't fade away by itself.
        /// </summary>
        public static void ShowAction(string title, string message, string button, Action onButton)
        {
            Get().Push(title, message, button, onButton);
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
            // one click area per notice, so taps between them reach the game
            for (int i = 0; i < Mice.Length; i++) Mice[i] = ListenForMouse(new Rectangle(0, 0, 0, 0), OnMouse);
        }

        private void Push(string title, string message, string button = null, Action onButton = null)
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
                Life = button != null ? double.MaxValue : Math.Min(16, 6 + message.Length * 0.04),
                Button = button,
                OnButton = onButton,
            };
            t.Height = Pad * 2 + 18 + lines.Count * 14 + (button != null ? ButtonH + 8 : 0);
            Toasts.Add(t);
            while (Toasts.Count > MaxShown) Toasts.RemoveAt(0);
            try { FSO.HIT.HITVM.Get().PlaySoundEvent(FSO.Client.UI.Model.UISounds.Click); } catch { }
        }

        private List<string> Wrap(string text)
        {
            var lines = new List<string>();
            float width = W - Pad * 2;
            var paras = (text ?? "").Replace("\r", "").Split('\n');
            var lastPara = paras.Length > 0 ? paras[paras.Length - 1] : null;
            foreach (var para in paras)
            {
                var words = para.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var line = "";
                foreach (var w in words)
                {
                    var test = line.Length == 0 ? w : line + " " + w;
                    float size;
                    try { size = BodyStyle.MeasureString(test).X; } catch { size = test.Length * 6; }
                    if (size > width && line.Length > 0)
                    {
                        // (at most 8 lines - a long message is cut short, not a wall of text)
                        if (lines.Count == MaxLines - 1) { lines.Add(line + "..."); return lines; }
                        lines.Add(line); line = w;
                    }
                    else line = test;
                }
                if (line.Length > 0)
                {
                    if (lines.Count == MaxLines - 1 && para != lastPara) { lines.Add(line + "..."); return lines; }
                    lines.Add(line);
                }
                if (lines.Count >= MaxLines) return lines;
            }
            return lines;
        }

        /// <summary>The text, shortened with "..." until it fits the width.</summary>
        private static string Fit(string text, TextStyle style, float width)
        {
            text = text ?? "";
            try
            {
                if (style.MeasureString(text).X <= width) return text;
                while (text.Length > 1 && style.MeasureString(text + "...").X > width) text = text.Substring(0, text.Length - 1);
                return text.TrimEnd() + "...";
            }
            catch { return text; }
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
                        // its button
                        if (t.Button != null && !t.Closing && p.X >= W - Pad - ButtonW && p.X < W - Pad
                            && p.Y >= y + t.Height - Pad - ButtonH && p.Y < y + t.Height - Pad)
                        {
                            t.Closing = true;
                            try { FSO.HIT.HITVM.Get().PlaySoundEvent(FSO.Client.UI.Model.UISounds.Click); } catch { }
                            var act = t.OnButton;
                            FSO.Common.Utils.GameThread.NextUpdate((s2) => { try { act?.Invoke(); } catch (Exception e) { Console.WriteLine("[SimsRedux] notice button: " + e.Message); } });
                            break;
                        }
                        // a notice with a button only closes from its X; the others from a click anywhere
                        if (t.Button != null && !(p.X >= W - Pad - 20 && p.Y < y + Pad + 20)) break;
                        if (!t.Closing) t.Closing = true;
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
            // (touch controls: below the clock and the cutaway button in the top-right corner)
            Position = new Vector2(width - W - 12, (FSO.Common.FSOEnvironment.SoftwareKeyboard && !Simitone.Client.Utils.ReduxTouch.ReduxLayout) ? TouchTop : Top);
            double dt = 1.0 / Math.Max(1, FSO.Common.FSOEnvironment.RefreshRate);
            for (int i = Toasts.Count - 1; i >= 0; i--)
            {
                var t = Toasts[i];
                if (t.Closing) { t.Slide -= (float)(dt * 5); if (t.Slide <= 0) Toasts.RemoveAt(i); continue; }
                if (t.Slide < 1) t.Slide = Math.Min(1, t.Slide + (float)(dt * 5));
                if (!MouseOver) t.Age += dt;
                if (t.Age >= t.Life) t.Closing = true;
            }
            // the stack never runs off the bottom of the screen: the oldest go first
            var screenH = FSO.Client.UI.Framework.UIScreen.Current?.ScreenHeight ?? 768;
            int room = Math.Max(120, screenH - (int)Position.Y - 12);
            int h = 0;
            foreach (var t in Toasts) h += t.Height + Gap;
            for (int i = 0; i < Toasts.Count - 1 && h > room; i++)
                if (!Toasts[i].Closing) { Toasts[i].Closing = true; h -= Toasts[i].Height + Gap; }
            // only the notices take clicks (up to the last one that isn't leaving); the rest of the
            // corner is left to the game
            int y2 = 0;
            for (int i = 0; i < Mice.Length; i++)
            {
                if (i < Toasts.Count && !Toasts[i].Closing) Mice[i].Region = new Rectangle(0, y2, W, Toasts[i].Height);
                else Mice[i].Region = new Rectangle(0, 0, 0, 0);
                if (i < Toasts.Count) y2 += Toasts[i].Height + Gap;
            }
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
                TitleStyle.Shadow = fade > 0.95f; // (the shadow can't fade with it)
                DrawLocalString(batch, Fit(t.Title, TitleStyle, W - Pad * 2 - 22), new Vector2(x + Pad, y + Pad), TitleStyle, new Rectangle(0, 0, W - Pad * 2 - 20, 18), TextAlignment.Left | TextAlignment.Middle);
                XStyle.Color = Color.White * (0.8f * fade);
                DrawLocalString(batch, "X", new Vector2(x + W - Pad - 14, y + Pad), XStyle, new Rectangle(0, 0, 14, 18), TextAlignment.Center | TextAlignment.Middle);
                BodyStyle.Color = new Color(220, 226, 248) * fade;
                int ly = y + Pad + 18;
                foreach (var line in t.Lines)
                {
                    DrawLocalString(batch, line, new Vector2(x + Pad, ly), BodyStyle, new Rectangle(0, 0, W - Pad * 2, 14), TextAlignment.Left | TextAlignment.Middle);
                    ly += 14;
                }
                if (t.Button != null)
                {
                    var bx = x + W - Pad - ButtonW;
                    var by = y + t.Height - Pad - ButtonH;
                    DrawLocalTexture(batch, ReduxTheme.Box(ButtonW, ButtonH, 12, ReduxTheme.Rim, ReduxTheme.Hi, Color.White * 0.7f, 1), null, new Vector2(bx, by), Vector2.One, Color.White * fade);
                    TitleStyle.Color = Color.White * fade;
                    DrawLocalString(batch, t.Button, new Vector2(bx, by), TitleStyle, new Rectangle(0, 0, ButtonW, ButtonH), TextAlignment.Center | TextAlignment.Middle);
                }
                y += t.Height + Gap;
            }
        }
    }
}
