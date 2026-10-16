using FSO.Client;
using FSO.Client.UI.Controls;
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

        private const int W = 470, H = 404, Pad = 22, StripW = 426;
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
            Active = this;
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

            // the colour's hex code (type one in too) and the colour dropper
            int rowY = SwY + 2 * (SwH + SwGap) + 2;
            Hex = new UITextBox();
            Hex.BackgroundTextureReference = null;
            Hex.SetSize(96, 24);
            Hex.Position = new Vector2(Pad + 34, rowY);
            Hex.TextMargin = new Rectangle(6, 2, 6, 2);
            Hex.TextStyle = Hex.TextStyle.Clone();
            Hex.TextStyle.Size = 12;
            Hex.TextStyle.Color = Color.White;
            Hex.OnChange += (e) => HexTyped();
            Add(Hex);
            var pick = new UIReduxButton("Pick colour from lot", 190, 24);
            pick.TextSize = 11;
            pick.Position = new Vector2(W - Pad - 190, rowY);
            pick.OnButtonClick += (b) => StartPick();
            Add(pick);

            int y = rowY + 26 + 30;
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

        // ------------------------------------------------------------------
        // The hex code and the colour dropper
        // ------------------------------------------------------------------

        private UITextBox Hex;

        /// <summary>The colour as it shows on a mid-light surface (what the swatches and the hex code show).</summary>
        private Color ShownColour
        {
            get
            {
                float v = Brightness >= 0 ? 0.78f + 0.22f * Brightness : 0.78f * (1 + Brightness);
                return ReduxTint.FromHSV(Hue, Colourful, v);
            }
        }

        private void UpdateHex()
        {
            if (Hex == null) return;
            var c = ShownColour;
            Hex.CurrentText = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

        private void HexTyped()
        {
            var t = (Hex.CurrentText ?? "").Trim().TrimStart('#');
            if (t.Length == 3) t = "" + t[0] + t[0] + t[1] + t[1] + t[2] + t[2]; // #RGB short codes
            if (t.Length != 6) return;
            int rgb;
            if (!int.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out rgb)) return;
            SetFromColour(new Color((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255), false);
        }

        /// <summary>Hue, colourfulness and brightness from a colour (the reverse of ShownColour).</summary>
        private void SetFromColour(Color c, bool updateHex)
        {
            float h, s, v;
            ReduxTint.ToHSV(c, out h, out s, out v);
            Hue = h;
            Colourful = s;
            Brightness = v >= 0.78f ? (v - 0.78f) / 0.22f : v / 0.78f - 1;
            // (pure white / black would flatten every bit of shading: the White and Black swatches' limits)
            Brightness = Math.Max(-0.85f, Math.Min(0.85f, Brightness));
            Strength = 1f;
            SwatchIndex = -1;
            SyncStrips();
            Preview(updateHex);
        }

        /// <summary>The dropper: the window steps aside, you click something on the lot, and its colour comes in.</summary>
        private void StartPick()
        {
            var lot = Game.LotControl;
            if (lot == null) return;
            DropFocus();
            UIScreen.RemoveDialog(this);
            if (lot.CustomControl != null) { lot.CustomControl.Release(); lot.CustomControl = null; }
            var dropper = new UIReduxDropper(lot, null, null, null);
            dropper.OnEntity = (ent) =>
            {
                TakeColourFrom(ent);
                if (lot.CustomControl == dropper) { lot.CustomControl = null; dropper.Release(); }
            };
            dropper.OnRelease = () => { UIScreen.GlobalShowDialog(this, true); Preview(); };
            lot.CustomControl = dropper;
            Simitone.Client.UI.Controls.UIReduxToasts.Show("Pick a colour", "Click an object on your lot to use its colour. Esc goes back.");
        }

        /// <summary>An object's colour: its own tint if it has one from this window, otherwise the main colour of its picture.</summary>
        private void TakeColourFrom(VMEntity ent)
        {
            try
            {
                var lead = ent.MultitileGroup?.BaseObject ?? ent;
                var tint = (lead as VMGameObject)?.ReduxTint ?? 0;
                if ((tint & 3) == ReduxTint.ModeColour)
                {
                    int h; float c, b, st;
                    ReduxTint.UnpackColour(tint, out h, out c, out b, out st);
                    Hue = h; Colourful = c; Brightness = b; Strength = st;
                    SwatchIndex = -1;
                    SyncStrips();
                    return;
                }
                var avg = MainColour(lead);
                if (avg == null) return;
                var col = avg.Value;
                if (tint != 0) col = ReduxTint.Apply(col, tint); // tinted the older way: the colour it shows now
                SetFromColour(col, true);
            }
            catch (Exception e) { Console.WriteLine("[SimsRedux] tint dropper: " + e.Message); }
        }

        /// <summary>The average colour of an object's catalog picture (its backdrop left out).</summary>
        private static Color? MainColour(VMEntity ent)
        {
            var def = ent.MasterDefinition ?? ent.Object.OBJ;
            var bmp = ent.Object.Resource.Get<FSO.Files.Formats.IFF.Chunks.BMP>(def.CatalogStringsID);
            var tex = bmp?.GetTexture(GameFacade.GraphicsDevice);
            if (tex == null || tex.Width < 2) return null;
            var data = new Color[tex.Width * tex.Height];
            tex.GetData(data);
            int w = tex.Width >= tex.Height * 1.5f ? tex.Width / 2 : tex.Width; // (the first of the two pictures)
            var back = data[0];
            long r = 0, g = 0, b = 0, n = 0;
            for (int y = 0; y < tex.Height; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = data[y * tex.Width + x];
                    if (c.A < 128) continue;
                    if (Math.Abs(c.R - back.R) + Math.Abs(c.G - back.G) + Math.Abs(c.B - back.B) < 40) continue;
                    r += c.R; g += c.G; b += c.B; n++;
                }
            if (n == 0) return null;
            return new Color((int)(r / n), (int)(g / n), (int)(b / n));
        }

        private uint Current
        {
            get { return ReduxTint.PackColour((int)Hue, Colourful, Brightness, Strength); }
        }

        /// <summary>Show the colour on the lot straight away (only on this screen until Apply).</summary>
        private void Preview() { Preview(true); }

        private void Preview(bool updateHex)
        {
            if (updateHex) UpdateHex();
            // the lot follows a few times a second (each new colour is a fresh set of pictures)
            PreviewDirty = true;
            StrengthStrip.HueForGradient = Hue;
            StrengthStrip.Colourful = Colourful;
            BrightStrip.HueForGradient = Hue;
            BrightStrip.Colourful = Colourful;
        }

        private bool PreviewDirty;
        private DateTime LastWorldPreview = DateTime.MinValue;
        private HashSet<uint> Previewed = new HashSet<uint>();

        /// <summary>The object on the lot shown in this colour (on this screen only).</summary>
        private void ShowOnLot(uint tint)
        {
            PreviewDirty = false;
            LastWorldPreview = DateTime.UtcNow;
            var obj = Target;
            if (obj == null) return;
            Previewed.Add(tint);
            foreach (var part in obj.MultitileGroup.Objects)
            {
                var comp = part.WorldUI as ObjectComponent;
                if (comp != null) comp.Tint = tint;
            }
        }

        private void DropFocus()
        {
            try
            {
                var im = GameFacade.Screens?.inputManager;
                if (im != null && Hex != null && im.GetFocus() == Hex) im.SetFocus(null);
            }
            catch { }
        }

        private void Send(uint tint)
        {
            if (Target == null) return;
            Game.vm.SendCommand(new VMNetTintObjectCmd { ObjectID = ObjectID, Tint = tint });
        }

        private void Apply()
        {
            var tint = Current;
            // back to how it really is: the colour comes from the (synced, saved) command, so if it's
            // refused the object doesn't keep a colour only this screen can see
            ShowOnLot(OriginalTint);
            Send(tint);
            Previewed.Remove(tint);
            Close();
        }

        private void CancelAndClose()
        {
            ShowOnLot(OriginalTint);
            Close();
        }

        private void Close()
        {
            DropFocus();
            UIScreen.RemoveDialog(this);
            if (Active == this) Active = null;
            // the pictures made while trying colours aren't needed any more
            Previewed.Remove(OriginalTint);
            try { ReduxTint.Forget(Previewed); } catch { }
            Previewed.Clear();
        }

        /// <summary>The Tint window that's open (also while it steps aside for the colour dropper).</summary>
        public static UIClassicTintDialog Active;

        public override void Update(UpdateState state)
        {
            Position = new Vector2((int)((GlobalSettings.Default.GraphicsWidth - W) / 2), 30);
            if (state.NewKeys.Contains(Microsoft.Xna.Framework.Input.Keys.Escape)) { CancelAndClose(); return; }
            if (Target == null) { Close(); return; } // object gone (sold, swapped...)
            if (PreviewDirty && (DateTime.UtcNow - LastWorldPreview).TotalMilliseconds >= 120) ShowOnLot(Current);
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

            // the hex box and a little patch of the colour
            int rowY = SwY + 2 * (SwH + SwGap) + 2;
            DrawLocalTexture(batch, ReduxTheme.Box(26, 24, 6, Color.White, Color.White, Color.Transparent, 0), null, new Vector2(Pad, rowY), Vector2.One, ShownColour);
            DrawLocalTexture(batch, ReduxTheme.Box(26, 24, 6, Color.Transparent, Color.Transparent, new Color(20, 20, 40), 1), new Vector2(Pad, rowY));
            DrawLocalTexture(batch, ReduxTheme.Box(96, 24, 8, ReduxTheme.Deeper, ReduxTheme.Deep, ReduxTheme.Rim, 1), new Vector2(Pad + 34, rowY));

            int y = rowY + 26 + 30;
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
