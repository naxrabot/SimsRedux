using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Client.UI.Model;
using FSO.Common.Rendering.Framework.IO;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using FSO.HIT;
using FSO.HIT.Model;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.UI.Screens;
using Simitone.Client.Utils;
using System;
using System.Collections.Generic;

namespace Simitone.Client.UI.Panels.Desktop
{
    /// <summary>
    /// SimsRedux: the Graphics, Sound and Play pages of the classic Options mode. They fill the
    /// bar to the right of the Save / Neighborhood / Quit buttons, like the original: a picture
    /// of the setting under the mouse on the left, the settings in rows on the right.
    ///
    ///   Graphics: Lighting, Smooth Zoom, Anti-Aliasing, Screen (window / full screen)
    ///   Sound:    Music, Sound Effects, Voices, Ambience
    ///   Play:     Camera controls, Screen-edge scrolling
    ///   Interface (a tab on the Graphics page): Classic / Redux look, UI colour, UI size
    /// </summary>
    public class UIClassicOptionsPages : UIContainer
    {
        public enum Page { None, Graphics, Sound, Play, Interface }
        public Page Current { get; private set; } = Page.None;

        private TS1GameScreen Game;
        private Texture2D Px;
        private TextStyle Label, Small;
        private static readonly Color TextColor = new Color(220, 220, 250);

        private const int BarY = 83;
        private const int PictureX = 452, RowX = 590, LabelW = 118, FirstRowY = 89, RowStep = 23;

        private class Row
        {
            public string Label, Popup;
            public int Y;
        }
        private List<Row> Rows = new List<Row>();
        private List<UIElement> Controls = new List<UIElement>();
        private Row HoverRow;
        private string Note;

        public UIClassicOptionsPages(TS1GameScreen game)
        {
            Game = game;
            Px = TextureGenerator.GetPxWhite(GameFacade.GraphicsDevice);
            Label = TextStyle.DefaultLabel.Clone(); Label.Size = 11; Label.Color = TextColor;
            Small = TextStyle.DefaultLabel.Clone(); Small.Size = 9; Small.Color = new Color(170, 170, 220);

            // Graphics / Interface tabs (shown on those two pages, above the picture)
            for (int i = 0; i < 2; i++)
            {
                var tab = (i == 0) ? Page.Graphics : Page.Interface;
                ListenForMouse(new Rectangle(PictureX, TabY + i * (TabH + 2), TabW, TabH), (type, state) =>
                {
                    if (!Visible || (Current != Page.Graphics && Current != Page.Interface)) return;
                    if (type == UIMouseEventType.MouseDown && Current != tab)
                    {
                        HITVM.Get().PlaySoundEvent(UISounds.Click);
                        TabPending = tab;
                    }
                });
            }
        }

        private const int TabY = 86, TabW = 104, TabH = 15;
        private Page TabPending = Page.None;

        public override void Update(FSO.Common.Rendering.Framework.Model.UpdateState state)
        {
            // switch tabs outside the click itself (the rows are rebuilt)
            if (TabPending != Page.None) { var p = TabPending; TabPending = Page.None; Show(p); }
            base.Update(state);
        }

        public void Show(Page page)
        {
            foreach (var c in Controls) Remove(c);
            Controls.Clear();
            Rows.Clear();
            HoverRow = null;
            Note = null;
            Current = page;
            switch (page)
            {
                case Page.Graphics: BuildGraphics(); break;
                case Page.Sound: BuildSound(); break;
                case Page.Play: BuildPlay(); break;
                case Page.Interface: BuildInterface(); break;
            }
        }

        // ------------------------------------------------------------------
        // Pages
        // ------------------------------------------------------------------

        private void BuildGraphics()
        {
            var g = GlobalSettings.Default;
            AddChoices("Lighting", "PopupOptLighting", new[] { "Off", "Low", "High", "Ultra" },
                () => g.LightingMode, (v) => { g.LightingMode = v; ReduxSettings.Set("lighting", v); ApplyWorldConfig(); });
            AddCheck("Smooth Zoom", "PopupOptAutoSnap",
                () => g.SmoothZoom, (v) => { g.SmoothZoom = v; ReduxSettings.Set("smoothzoom", v); ApplyWorldConfig(); });
            AddCheck("Anti-Aliasing", "PopupOptAntiAlias",
                () => g.AntiAlias > 0, (v) => { g.AntiAlias = v ? 1 : 0; ReduxSettings.Set("antialias", g.AntiAlias); ApplyWorldConfig(); });
            if (ReduxPlatform.SetFullScreen != null)
            {
                // browser: the page's own full screen, straight away
                AddChoices("Screen", "PopupOptTransUI", new[] { "Window", "Full Screen" },
                    () => (ReduxPlatform.IsFullScreen?.Invoke() ?? false) ? 1 : 0, (v) => ReduxPlatform.SetFullScreen(v == 1));
                Note = "Esc also leaves full screen.";
            }
            else
            {
                AddChoices("Screen", "PopupOptTransUI", new[] { "Window", "Full Screen" },
                    () => g.Windowed ? 0 : 1, (v) => SetFullScreen(v == 1));
                Note = "Screen changes start next time you open the game.";
            }
        }

        private void BuildInterface()
        {
            AddChoices("Look", "PopupOptTransUI", new[] { "Redux", "Classic" },
                () => ReduxTheme.Redux ? 0 : 1, (v) => ReduxTheme.Redux = v == 0);
            var row = NewRow("UI Colour", "PopupOptTransUI");
            var sw = new UIReduxSwatches();
            sw.Position = new Vector2(RowX + LabelW, row.Y + 2);
            sw.OnHover = () => HoverRow = row;
            Add(sw); Controls.Add(sw);
            var sizes = new List<string>();
            foreach (var v in ReduxTheme.ScaleChoices) sizes.Add(v == 0 ? "Auto" : v + "%");
            AddChoices("UI Size", "PopupOptTransUI", sizes.ToArray(),
                () => Math.Max(0, Array.IndexOf(ReduxTheme.ScaleChoices, ReduxTheme.Scale)),
                (v) => ReduxTheme.Scale = ReduxTheme.ScaleChoices[v]);
            Note = "Auto fits the panel to your screen. Classic uses the original art as it was.";
        }

        private void BuildSound()
        {
            var g = GlobalSettings.Default;
            AddSlider("Music", "PopupOptMusic", () => g.MusicVolume, (v) => { g.MusicVolume = (byte)v; ApplyVolumes(); });
            AddSlider("Sound Effects", "PopupOptSFX", () => g.FXVolume, (v) => { g.FXVolume = (byte)v; ApplyVolumes(); });
            AddSlider("Voices", "PopupOptVox", () => g.VoxVolume, (v) => { g.VoxVolume = (byte)v; ApplyVolumes(); });
            AddSlider("Ambience", "PopupOptSimInBack", () => g.AmbienceVolume, (v) => { g.AmbienceVolume = (byte)v; ApplyVolumes(); });
        }

        private void BuildPlay()
        {
            AddChoices("Camera", "PopupOptAutoCenter", new[] { "Sims 3 style", "Classic" },
                () => ReduxSettings.ClassicCamera ? 1 : 0, (v) => ReduxSettings.ClassicCamera = v == 1);
            // SimsRedux: how smart Sims are on their own (single player - online lots are always Smartest)
            AddChoices("Sim Smarts", "PopupOptAutoCenter", new[] { "Off", "Sims 1", "Smarter", "Smartest" },
                () => ReduxSettings.Autonomy + 1, (v) => ReduxSettings.Autonomy = v - 1);
            AddCheck("Edge Scrolling", "PopupOptEdgeScroll",
                () => ReduxSettings.EdgeScroll, (v) => { ReduxSettings.EdgeScroll = v; GlobalSettings.Default.EdgeScroll = v; GlobalSettings.Default.Save(); });
            if (ReduxPlatform.ChangeGameFolder != null)
            {
                // browser: choose a different Sims folder (the files are kept in the browser)
                AddAction("Sims Folder", "PopupOptEdgeScroll", "Change...", () =>
                    UIClassicDialog.YesNo("Change Sims Folder",
                        "Choose a different Sims folder?\n\nThe Sims files stored in this browser are removed and the game restarts. "
                        + "Homes and families saved in the game go with them.",
                        () => ReduxPlatform.ChangeGameFolder()));
            }
            if (!ReduxTown.Active && !Game.IsGuest)
            {
                // start the single-player neighbourhood over (asked again on the neighbourhood screen)
                AddAction("Neighborhood", "PopupOptEdgeScroll", "Start Fresh...", () => ReduxFreshHood.Ask(false));
            }
            Note = "Sims 3 camera: WASD / arrows move, Q E rotate, Z X zoom, right-drag grabs the ground.";
        }

        // ------------------------------------------------------------------
        // Applying settings
        // ------------------------------------------------------------------

        private void ApplyWorldConfig()
        {
            var settings = GlobalSettings.Default;
            settings.Save();
            FSO.LotView.WorldConfig.Current = new FSO.LotView.WorldConfig()
            {
                LightingMode = settings.LightingMode,
                SmoothZoom = settings.SmoothZoom,
                SurroundingLots = settings.SurroundingLotMode,
                AA = settings.AntiAlias,
                Weather = settings.Weather,
                Directional = settings.DirectionalLight3D,
                Complex = settings.ComplexShaders,
                EnableTransitions = settings.EnableTransitions
            };
            try { Game.vm?.Context?.World?.ChangedWorldConfig(GameFacade.GraphicsDevice); }
            catch (Exception e) { Console.WriteLine("[SimsRedux] Graphics change: " + e.Message); }
        }

        private void SetFullScreen(bool full)
        {
            var g = GlobalSettings.Default;
            g.Windowed = !full;
            try
            {
                var mode = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
                if (full)
                {
                    g.GraphicsWidth = mode.Width;
                    g.GraphicsHeight = mode.Height;
                }
                else
                {
                    // a comfortable window that fits the screen
                    g.GraphicsWidth = Math.Min(1280, mode.Width - 80);
                    g.GraphicsHeight = Math.Min(800, mode.Height - 120);
                }
            }
            catch { }
            g.Save();
        }

        private void ApplyVolumes()
        {
            var g = GlobalSettings.Default;
            var hit = HITVM.Get();
            hit.SetMasterVolume(HITVolumeGroup.FX, g.FXVolume / 10f);
            hit.SetMasterVolume(HITVolumeGroup.MUSIC, g.MusicVolume / 10f);
            hit.SetMasterVolume(HITVolumeGroup.VOX, g.VoxVolume / 10f);
            hit.SetMasterVolume(HITVolumeGroup.AMBIENCE, g.AmbienceVolume / 10f);
            g.Save();
        }

        // ------------------------------------------------------------------
        // Rows
        // ------------------------------------------------------------------

        private Row NewRow(string label, string popup)
        {
            var row = new Row { Label = label, Popup = popup, Y = FirstRowY + Rows.Count * RowStep };
            Rows.Add(row);
            return row;
        }

        private void AddChoices(string label, string popup, string[] choices, Func<int> get, Action<int> set)
        {
            var row = NewRow(label, popup);
            var art = TS1UIGraphics.Get("cpanel/Buttons/optradio.bmp");
            int x = RowX + LabelW;
            for (int i = 0; i < choices.Length; i++)
            {
                var index = i;
                var t = new UIClassicOptionToggle(art, choices[i], Label);
                t.Position = new Vector2(x, row.Y);
                t.IsOn = () => get() == index;
                t.OnClick = () => { set(index); };
                t.OnHover = () => HoverRow = row;
                Add(t); Controls.Add(t);
                x += 26 + Math.Max(28, choices[i].Length * 7);
            }
        }

        private void AddCheck(string label, string popup, Func<bool> get, Action<bool> set)
        {
            var row = NewRow(label, popup);
            var art = TS1UIGraphics.Get("cpanel/Buttons/optcheckbox.bmp");
            var t = new UIClassicOptionToggle(art, "", Label);
            t.Position = new Vector2(RowX + LabelW, row.Y);
            t.IsOn = get;
            t.OnClick = () => set(!get());
            t.OnHover = () => HoverRow = row;
            Add(t); Controls.Add(t);
        }

        /// <summary>A row with a single button-like choice that runs an action.</summary>
        private void AddAction(string label, string popup, string text, Action action)
        {
            var row = NewRow(label, popup);
            var art = TS1UIGraphics.Get("cpanel/Buttons/optradio.bmp");
            var t = new UIClassicOptionToggle(art, text, Label);
            t.Position = new Vector2(RowX + LabelW, row.Y);
            t.IsOn = () => false;
            t.OnClick = action;
            t.OnHover = () => HoverRow = row;
            Add(t); Controls.Add(t);
        }

        private void AddSlider(string label, string popup, Func<int> get, Action<int> set)
        {
            var row = NewRow(label, popup);
            var s = new UIClassicVolumeSlider(get, set);
            s.Position = new Vector2(RowX + LabelW, row.Y + 3);
            s.OnHover = () => HoverRow = row;
            Add(s); Controls.Add(s);
        }

        // ------------------------------------------------------------------
        // Draw
        // ------------------------------------------------------------------

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible || Current == Page.None) return;

            // picture of the setting under the mouse (or the first one)
            var row = HoverRow ?? (Rows.Count > 0 ? Rows[0] : null);
            bool tabs = Current == Page.Graphics || Current == Page.Interface;
            if (row != null)
            {
                var pic = TS1UIGraphics.Get("cpanel/" + row.Popup + ".bmp");
                if (pic != null && pic.Width > 1)
                {
                    // with the tabs showing, the picture sits under them, smaller
                    float top = tabs ? TabY + TabH * 2 + 6 : BarY + 4;
                    float s = (tabs ? 58f : 92f) / pic.Height;
                    DrawLocalTexture(batch, pic, null, new Vector2(PictureX, top), new Vector2(s, s));
                }
            }
            if (tabs)
            {
                for (int i = 0; i < 2; i++)
                {
                    var page = (i == 0) ? Page.Graphics : Page.Interface;
                    bool on = Current == page;
                    var y = TabY + i * (TabH + 2);
                    DrawLocalTexture(batch, Px, null, new Vector2(PictureX, y), new Vector2(TabW, TabH), on ? new Color(0, 160, 200) : new Color(0, 0, 60) * 0.8f);
                    DrawLocalTexture(batch, Px, null, new Vector2(PictureX, y), new Vector2(TabW, 1), on ? Color.Cyan : new Color(60, 60, 140));
                    Label.Color = on ? Color.White : TextColor;
                    DrawLocalString(batch, (i == 0) ? "Graphics" : "Interface", new Vector2(PictureX, y), Label, new Rectangle(0, 0, TabW, TabH), TextAlignment.Center | TextAlignment.Middle);
                    Label.Color = TextColor;
                }
            }

            // dark well behind the rows
            DrawLocalTexture(batch, Px, null, new Vector2(RowX - 6, BarY + 3), new Vector2(430, 94), new Color(0, 0, 50) * 0.6f);
            foreach (var r in Rows)
            {
                var c = (r == HoverRow) ? Color.White : TextColor;
                Label.Color = c;
                DrawLocalString(batch, r.Label, new Vector2(RowX, r.Y), Label, new Rectangle(0, 0, LabelW - 6, 20), TextAlignment.Left | TextAlignment.Middle);
                Label.Color = TextColor;
            }
            if (Note != null)
                DrawLocalString(batch, Note, new Vector2(RowX, FirstRowY + Rows.Count * RowStep), Small, new Rectangle(0, 0, 420, 18), TextAlignment.Left | TextAlignment.Middle);

            base.Draw(batch);
        }
    }

    /// <summary>
    /// SimsRedux: a Sims 1 option radio button or check box (optradio.bmp / optcheckbox.bmp).
    /// The sheets have 6 pictures: off, on (cyan), off-hover, on-hover, then two greyed ones.
    /// </summary>
    public class UIClassicOptionToggle : UIElement
    {
        public Func<bool> IsOn;
        public Action OnClick, OnHover;
        private Texture2D Art;
        private string Text;
        private TextStyle Style;
        private bool Hover;

        public UIClassicOptionToggle(Texture2D art, string text, TextStyle style)
        {
            Art = art;
            Text = text;
            Style = style;
            int w = 24 + Math.Max(0, text.Length * 7);
            ListenForMouse(new Rectangle(0, 0, w, 20), (type, state) =>
            {
                if (type == UIMouseEventType.MouseOver) { Hover = true; OnHover?.Invoke(); }
                else if (type == UIMouseEventType.MouseOut) Hover = false;
                else if (type == UIMouseEventType.MouseDown)
                {
                    HITVM.Get().PlaySoundEvent(UISounds.Click);
                    OnClick?.Invoke();
                }
            });
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            bool on = IsOn?.Invoke() ?? false;
            if (Art != null && Art.Width > 1)
            {
                int fw = Art.Width / 6;
                int frame = (on ? 1 : 0) + (Hover ? 2 : 0);
                DrawLocalTexture(batch, Art, new Rectangle(frame * fw, 0, fw, Art.Height), Vector2.Zero);
            }
            if (!string.IsNullOrEmpty(Text))
                DrawLocalString(batch, Text, new Vector2(23, 0), Style, new Rectangle(0, 0, 120, 20), TextAlignment.Left | TextAlignment.Middle);
        }
    }

    /// <summary>SimsRedux: a 0-10 volume bar. Click or drag along it.</summary>
    public class UIClassicVolumeSlider : UIElement
    {
        public Action OnHover;
        private Func<int> Get;
        private Action<int> Set;
        private bool Dragging;
        private const int Steps = 10, StepW = 18, H = 14;

        public UIClassicVolumeSlider(Func<int> get, Action<int> set)
        {
            Get = get;
            Set = set;
            ListenForMouse(new Rectangle(-6, -3, (Steps + 1) * StepW + 6, H + 6), (type, state) =>
            {
                if (type == UIMouseEventType.MouseOver) OnHover?.Invoke();
                else if (type == UIMouseEventType.MouseDown) { Dragging = true; Track(state); }
                else if (type == UIMouseEventType.MouseUp) Dragging = false;
            });
        }

        private void Track(UpdateState state)
        {
            var local = GlobalPoint(new Vector2(state.MouseState.X, state.MouseState.Y));
            int v = (int)Math.Round(local.X / StepW);
            v = Math.Max(0, Math.Min(Steps, v));
            if (v != Get()) Set(v);
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
            int value = Get();
            for (int i = 1; i <= Steps; i++)
            {
                int h = 4 + i;
                var c = (i <= value) ? new Color(70, 255, 110) : new Color(40, 40, 110);
                DrawLocalTexture(batch, px, null, new Vector2(i * StepW - 12, H - h), new Vector2(12, h), c);
            }
            // "off" mark at the start
            DrawLocalTexture(batch, px, null, new Vector2(-4, H - 3), new Vector2(8, 3), value == 0 ? Color.White : new Color(40, 40, 110));
        }
    }
}

namespace Simitone.Client.UI.Panels.Desktop
{
    /// <summary>SimsRedux: the UI colour choices - a row of colour squares, click one to use it.</summary>
    public class UIReduxSwatches : UIElement
    {
        public Action OnHover;
        private const int Size = 16, Step = 22;
        private int Hover = -1;
        private bool Inside;
        private TextStyle NameStyle;

        public UIReduxSwatches()
        {
            int n = ReduxTheme.Presets.Length;
            ListenForMouse(new Rectangle(0, 0, n * Step, Size), (type, state) =>
            {
                if (type == UIMouseEventType.MouseOut) { Hover = -1; Inside = false; return; }
                if (type == UIMouseEventType.MouseOver) { Inside = true; OnHover?.Invoke(); }
                var local = GlobalPoint(new Vector2(state.MouseState.X, state.MouseState.Y));
                int i = (int)(local.X / Step);
                Hover = (i >= 0 && i < n) ? i : -1;
                if (type == UIMouseEventType.MouseDown && Hover >= 0)
                {
                    HITVM.Get().PlaySoundEvent(UISounds.Click);
                    ReduxTheme.Base = ReduxTheme.Presets[Hover];
                }
            });
        }

        public override void Update(FSO.Common.Rendering.Framework.Model.UpdateState state)
        {
            base.Update(state);
            // follow the mouse while it is over the squares (there's no "mouse moved" event)
            if (Inside)
            {
                var local = GlobalPoint(new Vector2(state.MouseState.X, state.MouseState.Y));
                if (local.Y < 0 || local.Y > Size || local.X < 0 || local.X >= ReduxTheme.Presets.Length * Step) Hover = -1;
                else Hover = Math.Min(ReduxTheme.Presets.Length - 1, (int)(local.X / Step));
            }
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            var px = TextureGenerator.GetPxWhite(GameFacade.GraphicsDevice);
            var current = ReduxTheme.Base;
            for (int i = 0; i < ReduxTheme.Presets.Length; i++)
            {
                var c = ReduxTheme.Presets[i];
                bool on = c == current;
                int x = i * Step;
                var ring = on ? Color.White : (i == Hover ? Color.Cyan : new Color(20, 20, 60));
                DrawLocalTexture(batch, px, null, new Vector2(x - 2, -2), new Vector2(Size + 4, Size + 4), ring);
                DrawLocalTexture(batch, px, null, new Vector2(x, 0), new Vector2(Size, Size), c);
                DrawLocalTexture(batch, px, null, new Vector2(x, 0), new Vector2(Size, Size / 3), ReduxTheme.Shade(c, 0.25f));
            }
            if (Hover >= 0)
            {
                if (NameStyle == null) { NameStyle = TextStyle.DefaultLabel.Clone(); NameStyle.Size = 9; NameStyle.Color = Color.White; } // made once, not every frame
                DrawLocalString(batch, ReduxTheme.PresetNames[Hover], new Vector2(ReduxTheme.Presets.Length * Step + 6, 0), NameStyle, new Rectangle(0, 0, 90, Size), TextAlignment.Left | TextAlignment.Middle);
            }
        }
    }
}
