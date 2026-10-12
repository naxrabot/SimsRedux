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
using Simitone.Client.UI.Screens;
using Simitone.Client.Utils;
using System;
using System.Collections.Generic;

namespace Simitone.Client.UI.Panels.Desktop
{
    /// <summary>
    /// SimsRedux: the colour tint window. Two ways to recolour an object:
    ///   - Whole object: the whole thing takes on the new colour (light and dark parts are kept)
    ///   - One colour only: pick the colour to change (e.g. the blue fabric) and what it becomes
    /// The object on the lot changes live while you adjust; Apply keeps it, Cancel puts it back.
    /// </summary>
    public class UIClassicTintDialog : UIContainer
    {
        private TS1GameScreen Game;
        private short ObjectID;
        private uint OriginalTint;
        private int Mode = ReduxTint.ModeWhole;
        private float ToHue = 200, FromHue = 220, Range = 30, Strength = 0.6f;

        private const int W = 480, H = 330;
        private static readonly Color TextColor = new Color(205, 205, 240);
        private TextStyle Style, TitleStyle;
        private UITS1TileButton WholeBtn, RangeBtn;
        private UIClassicStrip NewHueStrip, StrengthStrip, FromHueStrip, RangeStrip;
        private string ObjectName = "";

        public UIClassicTintDialog(TS1GameScreen game, short objectID)
        {
            Game = game;
            ObjectID = objectID;
            Style = TextStyle.DefaultLabel.Clone(); Style.Size = 12; Style.Color = TextColor;
            TitleStyle = TextStyle.DefaultLabel.Clone(); TitleStyle.Size = 18; TitleStyle.Color = TextColor;

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
                    int m, th, fh, r; float st;
                    ReduxTint.Unpack(OriginalTint, out m, out th, out fh, out r, out st);
                    Mode = m; ToHue = th; FromHue = fh; Range = Math.Max(10, r); Strength = st;
                }
            }

            var tileTex = TS1UIGraphics.Get("cpanel/Buttons/ButtonTileDialog.bmp");
            WholeBtn = AddButton(tileTex, "Whole object", 40, 56, 190, () => SetMode(ReduxTint.ModeWhole));
            RangeBtn = AddButton(tileTex, "One colour only", 250, 56, 190, () => SetMode(ReduxTint.ModeRange));

            NewHueStrip = AddStrip(UIClassicStrip.Kind.Hue, 40, 128, (v) => { ToHue = v * 359; Preview(); });
            StrengthStrip = AddStrip(UIClassicStrip.Kind.Strength, 40, 186, (v) => { Strength = v; Preview(); });
            FromHueStrip = AddStrip(UIClassicStrip.Kind.Hue, 40, 186, (v) => { FromHue = v * 359; Preview(); });
            RangeStrip = AddStrip(UIClassicStrip.Kind.Range, 40, 244, (v) => { Range = 6 + v * 114; Preview(); });

            AddButton(tileTex, "Apply", 20, 282, 140, Apply);
            AddButton(tileTex, "Original colours", 170, 282, 160, () => { Send(0); Close(); });
            AddButton(tileTex, "Cancel", 340, 282, 120, CancelAndClose);

            SyncStrips();
            SetMode(Mode == ReduxTint.ModeNone ? ReduxTint.ModeWhole : Mode);
        }

        private VMGameObject Target
        {
            get { return Game.vm?.GetObjectById(ObjectID) as VMGameObject; }
        }

        private UITS1TileButton AddButton(Texture2D tex, string text, int x, int y, int width, Action click)
        {
            var btn = new UITS1TileButton(tex, text, width);
            btn.Position = new Vector2(x, y);
            btn.OnButtonClick += (b) => click();
            Add(btn);
            return btn;
        }

        private UIClassicStrip AddStrip(UIClassicStrip.Kind kind, int x, int y, Action<float> changed)
        {
            var strip = new UIClassicStrip(kind, 400, 22);
            strip.Position = new Vector2(x, y);
            strip.OnChange = changed;
            Add(strip);
            return strip;
        }

        private void SyncStrips()
        {
            NewHueStrip.Value = ToHue / 359f;
            StrengthStrip.Value = Strength;
            FromHueStrip.Value = FromHue / 359f;
            RangeStrip.Value = (Range - 6) / 114f;
        }

        private void SetMode(int mode)
        {
            Mode = mode;
            bool whole = mode == ReduxTint.ModeWhole;
            StrengthStrip.Visible = whole;
            FromHueStrip.Visible = !whole;
            RangeStrip.Visible = !whole;
            Preview();
        }

        private uint Current
        {
            get { return ReduxTint.Pack(Mode, (int)ToHue, (int)FromHue, (int)Range, Strength); }
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
            StrengthStrip.HueForGradient = ToHue;
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
            var px = TextureGenerator.GetPxWhite(GameFacade.GraphicsDevice);
            // navy box, like the other classic popups
            DrawLocalTexture(batch, px, null, new Vector2(6, 6), new Vector2(W, H), Color.Black * 0.35f);
            DrawLocalTexture(batch, px, null, Vector2.Zero, new Vector2(W, H), new Color(0, 0, 82));
            DrawLocalTexture(batch, px, null, Vector2.Zero, new Vector2(W, 2), new Color(120, 120, 200));
            DrawLocalTexture(batch, px, null, Vector2.Zero, new Vector2(2, H), new Color(120, 120, 200));
            DrawLocalTexture(batch, px, null, new Vector2(0, H - 2), new Vector2(W, 2), new Color(0, 0, 40));
            DrawLocalTexture(batch, px, null, new Vector2(W - 2, 0), new Vector2(2, H), new Color(0, 0, 40));

            DrawLocalString(batch, "Change Colour: " + ObjectName, new Vector2(20, 12), TitleStyle, new Rectangle(0, 0, W - 40, 30), TextAlignment.Left | TextAlignment.Middle);

            // which mode is on
            var active = (Mode == ReduxTint.ModeWhole) ? WholeBtn : RangeBtn;
            DrawOutline(batch, px, new Rectangle((int)active.X - 3, (int)active.Y - 3, 196, 39), Color.Cyan);

            DrawLocalString(batch, "New colour", new Vector2(40, 106), Style, new Rectangle(0, 0, 400, 20), TextAlignment.Left | TextAlignment.Middle);
            if (Mode == ReduxTint.ModeWhole)
                DrawLocalString(batch, "Strength", new Vector2(40, 164), Style, new Rectangle(0, 0, 400, 20), TextAlignment.Left | TextAlignment.Middle);
            else
            {
                DrawLocalString(batch, "Colour to change (pick the colour of the part you want to recolour)", new Vector2(40, 164), Style, new Rectangle(0, 0, 420, 20), TextAlignment.Left | TextAlignment.Middle);
                DrawLocalString(batch, "How similar a colour must be to change", new Vector2(40, 222), Style, new Rectangle(0, 0, 400, 20), TextAlignment.Left | TextAlignment.Middle);
            }
            base.Draw(batch);
        }

        private void DrawOutline(UISpriteBatch batch, Texture2D px, Rectangle r, Color c)
        {
            DrawLocalTexture(batch, px, null, new Vector2(r.X, r.Y), new Vector2(r.Width, 2), c);
            DrawLocalTexture(batch, px, null, new Vector2(r.X, r.Bottom - 2), new Vector2(r.Width, 2), c);
            DrawLocalTexture(batch, px, null, new Vector2(r.X, r.Y), new Vector2(2, r.Height), c);
            DrawLocalTexture(batch, px, null, new Vector2(r.Right - 2, r.Y), new Vector2(2, r.Height), c);
        }
    }

    /// <summary>SimsRedux: a click-and-drag colour bar (rainbow, strength or range).</summary>
    public class UIClassicStrip : UIElement
    {
        public enum Kind { Hue, Strength, Range }
        public Kind Type;
        public float Value; // 0-1
        public float HueForGradient = 200;
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
                    case Kind.Strength: c = ReduxTint.FromHSV(HueForGradient, t, 0.85f); break;
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
