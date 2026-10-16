using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.IO;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using FSO.LotView.Components;
using FSO.LotView.Utils;
using FSO.SimAntics;
using FSO.SimAntics.NetPlay.Model.Commands;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Screens;
using Simitone.Client.Utils;
using System;
using System.Collections.Generic;

namespace Simitone.Client.UI.Panels.Desktop
{
    /// <summary>
    /// SimsRedux: the Tint window (right-click an object in Buy or Build mode). The whole object takes
    /// on a new colour: pick one of the swatches (white and black included), then fine-tune it with
    /// Colour, Strength and Brightness. The object on the lot changes live while you adjust; Apply
    /// keeps it, Cancel puts it back, Original colours takes the tint off.
    /// </summary>
    public class UIClassicTintDialog : UIContainer
    {
        private TS1GameScreen Game;
        private short ObjectID;
        private uint OriginalTint;
        private float Hue = 200, Colourful = 0.85f, Brightness = 0, Strength = 0.75f;

        private const int W = 470, H = 372, Pad = 22, StripW = 426;
        private TextStyle Style, TitleStyle;
        private UIClassicStrip HueStrip, StrengthStrip, BrightStrip;
        private string ObjectName = "";
        private int SwatchIndex = -1;

        /// <summary>The swatches: name, hue, how colourful, brightness.</summary>
        private static readonly Tuple<string, float, float, float>[] Swatches =
        {
            Tuple.Create("Red", 0f, 0.85f, 0f), Tuple.Create("Orange", 28f, 0.85f, 0f), Tuple.Create("Yellow", 52f, 0.85f, 0.1f),
            Tuple.Create("Lime", 85f, 0.8f, 0f), Tuple.Create("Green", 125f, 0.8f, -0.1f), Tuple.Create("Teal", 172f, 0.75f, -0.1f),
            Tuple.Create("Sky blue", 200f, 0.6f, 0.2f), Tuple.Create("Blue", 225f, 0.85f, -0.05f), Tuple.Create("Navy", 230f, 0.8f, -0.5f),
            Tuple.Create("Purple", 275f, 0.75f, -0.1f), Tuple.Create("Pink", 330f, 0.45f, 0.3f), Tuple.Create("Magenta", 305f, 0.85f, 0f),
            Tuple.Create("Brown", 25f, 0.7f, -0.45f), Tuple.Create("Tan", 35f, 0.35f, 0.25f), Tuple.Create("Cream", 45f, 0.15f, 0.6f),
            Tuple.Create("Grey", 0f, 0f, 0f), Tuple.Create("White", 0f, 0f, 0.85f), Tuple.Create("Black", 0f, 0f, -0.85f),
        };
        private const int SwCols = 9, SwW = 42, SwH = 30, SwGap = 5, SwY = 56;

        public UIClassicTintDialog(TS1GameScreen game, short objectID)
        {
            Game = game;
            ObjectID = objectID;
            Style = TextStyle.DefaultLabel.Clone(); Style.Size = 12; Style.Color = new Color(220, 226, 250);
            TitleStyle = TextStyle.DefaultLabel.Clone(); TitleStyle.Size = 17; TitleStyle.Color = Color.White; TitleStyle.Shadow = true;

            var obj = Target;
            if (obj != null)
            {
                OriginalTint = obj.ReduxTint;
                ObjectName = obj.ToString();
                try
                {
                    var def = obj.MasterDefinition ?? obj.Object.OBJ;
                    var name = obj.Object.Resource.Get<FSO.Files.Formats.IFF.Chunks.CTSS>(def.CatalogStringsID)?.GetString(0);
                    if (!string.IsNullOrEmpty(name)) ObjectName = name;
                }
                catch { }
                if (OriginalTint != 0)
                {
                    if ((OriginalTint & 3) == ReduxTint.ModeColour)
                    {
                        int h; float c, b, st;
                        ReduxTint.UnpackColour(OriginalTint, out h, out c, out b, out st);
                        Hue = h; Colourful = c; Brightness = b; Strength = st;
                    }
                    else
                    {
                        // a tint from before the colour picker: start from its colour
                        int m, th, fh, r; float st;
                        ReduxTint.Unpack(OriginalTint, out m, out th, out fh, out r, out st);
                        Hue = th; Strength = Math.Max(0.4f, st);
                    }
                }
            }

            // the swatches (clickable cells)
            for (int i = 0; i < Swatches.Length; i++)
            {
                var index = i;
                var cell = new UIClassicSwatch(SwW, SwH, SwatchColour(i), Swatches[i].Item1);
                cell.Position = new Vector2(Pad + (i % SwCols) * (SwW + SwGap), SwY + (i / SwCols) * (SwH + SwGap));
                cell.OnClick = () => PickSwatch(index);
                Add(cell);
            }

            int y = SwY + 2 * (SwH + SwGap) + 30;
            HueStrip = AddStrip(UIClassicStrip.Kind.Hue, Pad, y, (v) => { Hue = v * 359; if (Colourful < 0.2f) Colourful = 0.8f; SwatchIndex = -1; Preview(); });
            y += 56;
            StrengthStrip = AddStrip(UIClassicStrip.Kind.Strength, Pad, y, (v) => { Strength = v; Preview(); });
            y += 56;
            BrightStrip = AddStrip(UIClassicStrip.Kind.Brightness, Pad, y, (v) => { Brightness = v * 2 - 1; Preview(); });

            AddButton("Apply", Pad, H - 44, 130, Apply);
            AddButton("Original colours", (W - 160) / 2, H - 44, 160, () => { Send(0); Close(); });
            AddButton("Cancel", W - Pad - 130, H - 44, 130, CancelAndClose);

            SyncStrips();
            Preview();
        }

        private static Color SwatchColour(int i)
        {
            var sw = Swatches[i];
            float v = sw.Item4 >= 0 ? 0.78f + 0.22f * sw.Item4 : 0.78f * (1 + sw.Item4);
            return ReduxTint.FromHSV(sw.Item2, sw.Item3, v);
        }

        private void PickSwatch(int i)
        {
            var sw = Swatches[i];
            SwatchIndex = i;
            Hue = sw.Item2; Colourful = sw.Item3; Brightness = sw.Item4;
            Strength = 1f;
            SyncStrips();
            Preview();
        }

        private VMGameObject Target
        {
            get { return Game.vm?.GetObjectById(ObjectID) as VMGameObject; }
        }

        private void AddButton(string text, int x, int y, int width, Action click)
        {
            var btn = new UIReduxButton(text, width, 28);
            btn.Position = new Vector2(x, y);
            btn.OnButtonClick += (b) => click();
            Add(btn);
        }

        private UIClassicStrip AddStrip(UIClassicStrip.Kind kind, int x, int y, Action<float> changed)
        {
            var strip = new UIClassicStrip(kind, StripW, 20);
            strip.Position = new Vector2(x, y);
            strip.OnChange = changed;
            Add(strip);
            return strip;
        }

        private void SyncStrips()
        {
            HueStrip.Value = Hue / 359f;
            StrengthStrip.Value = Strength;
            BrightStrip.Value = (Brightness + 1) / 2f;
        }

        private uint Current
        {
            get { return ReduxTint.PackColour((int)Hue, Colourful, Brightness, Strength); }
        }

        /// <summary>Show the colour on the lot straight away (only on this screen until Apply).</summary>
        private void Preview()
        {
            var obj = Target;
            if (obj == null) return;
            foreach (var part in obj.MultitileGroup.Objects)
            {
                var comp = part.WorldUI as ObjectComponent;
                if (comp != null) comp.Tint = Current;
            }
            StrengthStrip.HueForGradient = Hue;
            StrengthStrip.Colourful = Colourful;
            BrightStrip.HueForGradient = Hue;
            BrightStrip.Colourful = Colourful;
        }

        private void Send(uint tint)
        {
            if (Target == null) return;
            Game.vm.SendCommand(new VMNetTintObjectCmd { ObjectID = ObjectID, Tint = tint });
        }

        private void Apply()
        {
            Send(Current);
            Close();
        }

        private void CancelAndClose()
        {
            var obj = Target;
            if (obj != null)
                foreach (var part in obj.MultitileGroup.Objects)
                {
                    var comp = part.WorldUI as ObjectComponent;
                    if (comp != null) comp.Tint = OriginalTint;
                }
            Close();
        }

        private void Close()
        {
            UIScreen.RemoveDialog(this);
        }

        public override void Update(UpdateState state)
        {
            Position = new Vector2((int)((GlobalSettings.Default.GraphicsWidth - W) / 2), 30);
            if (state.NewKeys.Contains(Microsoft.Xna.Framework.Input.Keys.Escape)) { CancelAndClose(); return; }
            if (Target == null) { Close(); return; } // object gone (sold, swapped...)
            base.Update(state);
        }

        public override Rectangle GetBounds()
        {
            return new Rectangle(0, 0, W, H);
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            // the Redux window: rounded, in the UI colour
            DrawLocalTexture(batch, ReduxTheme.Box(W, H, 14, Color.Black * 0.35f, Color.Black * 0.35f, Color.Transparent, 0), new Vector2(4, 6));
            DrawLocalTexture(batch, ReduxTheme.Box(W, H, 14, ReduxTheme.Hi, ReduxTheme.Deeper, ReduxTheme.Rim, 2), Vector2.Zero);
            DrawLocalString(batch, "Tint: " + ObjectName, new Vector2(Pad, 12), TitleStyle, new Rectangle(0, 0, W - Pad * 2, 30), TextAlignment.Left | TextAlignment.Middle);

            // the chosen swatch is outlined
            if (SwatchIndex >= 0)
            {
                var p = new Vector2(Pad + (SwatchIndex % SwCols) * (SwW + SwGap) - 3, SwY + (SwatchIndex / SwCols) * (SwH + SwGap) - 3);
                DrawLocalTexture(batch, ReduxTheme.Box(SwW + 6, SwH + 6, 8, Color.Transparent, Color.Transparent, ReduxTheme.Glow, 2), p);
            }

            int y = SwY + 2 * (SwH + SwGap) + 30;
            DrawLocalString(batch, "Colour", new Vector2(Pad, y - 22), Style, new Rectangle(0, 0, StripW, 20), TextAlignment.Left | TextAlignment.Middle);
            DrawLocalString(batch, "Strength", new Vector2(Pad, y + 56 - 22), Style, new Rectangle(0, 0, StripW, 20), TextAlignment.Left | TextAlignment.Middle);
            DrawLocalString(batch, "Brightness", new Vector2(Pad, y + 112 - 22), Style, new Rectangle(0, 0, StripW, 20), TextAlignment.Left | TextAlignment.Middle);
            base.Draw(batch);
        }
    }

    /// <summary>SimsRedux: one colour swatch in the Tint window.</summary>
    public class UIClassicSwatch : UIElement
    {
        public Action OnClick;
        private int W, H;
        private Color Colour;
        private bool Hover;

        public UIClassicSwatch(int w, int h, Color colour, string name)
        {
            W = w; H = h; Colour = colour;
            ListenForMouse(new Rectangle(0, 0, w, h), (type, state) =>
            {
                if (type == UIMouseEventType.MouseOver) Hover = true;
                else if (type == UIMouseEventType.MouseOut) Hover = false;
                else if (type == UIMouseEventType.MouseUp && Hover)
                {
                    FSO.HIT.HITVM.Get().PlaySoundEvent(FSO.Client.UI.Model.UISounds.Click);
                    OnClick?.Invoke();
                }
            });
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            DrawLocalTexture(batch, ReduxTheme.Box(W, H, 7, Colour, Color.Lerp(Colour, Color.Black, 0.18f), Hover ? ReduxTheme.Glow : new Color(20, 20, 40), Hover ? 2 : 1), Vector2.Zero);
        }
    }

    /// <summary>SimsRedux: a click-and-drag colour bar (rainbow, strength or range).</summary>
    public class UIClassicStrip : UIElement
    {
        public enum Kind { Hue, Strength, Range, Brightness }
        public Kind Type;
        public float Value; // 0-1
        public float HueForGradient = 200;
        public float Colourful = 0.85f;
        public Action<float> OnChange;
        private int Width, Height;
        private bool Dragging;

        public UIClassicStrip(Kind kind, int width, int height)
        {
            Type = kind;
            Width = width;
            Height = height;
            ListenForMouse(new Rectangle(0, -4, width, height + 8), (type, state) =>
            {
                if (!Visible) return;
                if (type == UIMouseEventType.MouseDown) { Dragging = true; Track(state); }
                else if (type == UIMouseEventType.MouseUp) Dragging = false;
            });
        }

        private void Track(UpdateState state)
        {
            var local = GlobalPoint(new Vector2(state.MouseState.X, state.MouseState.Y));
            var v = Math.Max(0, Math.Min(1, local.X / Width));
            if (Math.Abs(v - Value) > 0.0001f)
            {
                Value = v;
                OnChange?.Invoke(v);
            }
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            if (Dragging)
            {
                if (state.MouseState.LeftButton != Microsoft.Xna.Framework.Input.ButtonState.Pressed) Dragging = false;
                else Track(state);
            }
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            var px = TextureGenerator.GetPxWhite(GameFacade.GraphicsDevice);
            int steps = Width / 4;
            for (int i = 0; i < steps; i++)
            {
                float t = i / (float)(steps - 1);
                Color c;
                switch (Type)
                {
                    case Kind.Hue: c = ReduxTint.FromHSV(t * 359, 0.85f, 0.95f); break;
                    case Kind.Strength: c = ReduxTint.FromHSV(HueForGradient, t * Math.Max(0.15f, Colourful), 0.85f); break;
                    case Kind.Brightness:
                        {
                            // black - the colour - white
                            var mid = ReduxTint.FromHSV(HueForGradient, Colourful, 0.78f);
                            c = t < 0.5f ? Color.Lerp(Color.Black, mid, t * 2) : Color.Lerp(mid, Color.White, (t - 0.5f) * 2);
                            break;
                        }
                    default: c = Color.Lerp(new Color(40, 40, 120), new Color(180, 180, 240), t); break;
                }
                DrawLocalTexture(batch, px, null, new Vector2(i * 4, 0), new Vector2(4, Height), c);
            }
            // border and marker
            DrawLocalTexture(batch, px, null, new Vector2(-1, -1), new Vector2(Width + 2, 1), new Color(120, 120, 200));
            DrawLocalTexture(batch, px, null, new Vector2(-1, Height), new Vector2(Width + 2, 1), new Color(0, 0, 40));
            var mx = Value * Width;
            DrawLocalTexture(batch, px, null, new Vector2(mx - 2, -4), new Vector2(5, Height + 8), Color.Black);
            DrawLocalTexture(batch, px, null, new Vector2(mx - 1, -3), new Vector2(3, Height + 6), Color.White);
        }
    }
}
