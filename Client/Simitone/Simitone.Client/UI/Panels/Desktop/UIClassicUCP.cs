using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common;
using FSO.Common.Rendering.Framework.IO;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Screens;
using Simitone.Client.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Client.UI.Panels.Desktop
{
    /// <summary>
    /// SimsRedux: the original Sims 1 control panel "wing" (bottom-left), built from the
    /// player's UIGraphics.far: Live / Buy / Build / Camera / Options buttons, floor and wall
    /// view buttons, zoom and rotate, speed, clock, friends and money, plus the bottom bar.
    ///
    /// It reuses all of Simitone's desktop panel logic (it inherits UIDesktopUCP) and only
    /// swaps the pictures and layout. Every position was measured from screenshots of the
    /// original game. Coordinates are in the original 1:1 layout with the wing at (0,0);
    /// the whole panel is scaled up on bigger screens.
    /// </summary>
    public class UIClassicUCP : UIDesktopUCP
    {
        private TS1GameScreen Screen;
        private Texture2D WingBack, BarBack;
        private Texture2D[] ModePatches;
        private UIMainPanelMode CurrentMode = UIMainPanelMode.LIVE;
        private UITS1Button CameraBtn, HelpBtn;
        private UILabel ClockLabel, FriendsCount;

        // Options mode (bar contents)
        private UITS1Button[] OptionButtons;
        private Texture2D OptLineH, OptLineV;

        /// <summary>Simitone's mode panel. Hidden while the classic bar shows its own contents.</summary>
        public UIMainPanel ModeContents;

        // Live mode (bar contents)
        private UIClassicLivePanel LivePanel;
        // Buy mode (bar contents)
        private UIClassicBuyPanel BuyPanel;
        // Build mode (bar contents)
        private UIClassicBuildPanel BuildPanel;
        // Options mode: Graphics / Sound / Play pages
        private UIClassicOptionsPages OptionPages;
        private UITS1Button GraphicsBtn, SoundBtn, PlayBtn;

        public const int WingW = 220, WingH = 183;
        private const int BarX = 218, BarY = 83, BarH = 100;
        private static readonly Color LcdGreen = new Color(70, 255, 110);

        public float UIScale = 1f;

        // ---- SimsRedux touch layout ("B": the Sims 1 panel, grown for fingers) ----
        public enum PanelLayout { Classic, TouchWide, TouchTall }
        /// <summary>Classic (PC), TouchWide (phone/tablet held sideways) or TouchTall (held upright).</summary>
        public PanelLayout Layout { get; private set; } = PanelLayout.Classic;
        public bool Touch { get { return Layout != PanelLayout.Classic; } }
        /// <summary>Where the bar's contents start and end (in the panel's own units).</summary>
        public float ContentX = BarX, BarRight = 1024;
        /// <summary>On the screen: the top of everything the panel shows, the top and left of the bar's contents.</summary>
        public float PanelTopScreen, BarTopScreen, BarLeftScreen;
        private float LayoutWu = -1;
        private Dictionary<UIElement, Vector4> ClassicPlace = new Dictionary<UIElement, Vector4>();
        private UIMouseEventRef BarCatcher;
        private UIReduxTextButton BackBtn;
        private UIElement[] RightBlock;
        private bool RightBlockShown = true;
        private float RX, RY;
        private Vector2 DiamondC;
        private Rectangle ClockBox;

        public UIClassicUCP(TS1GameScreen screen) : base(screen)
        {
            Screen = screen;

            // throw away Simitone's pictures, keep its logic
            foreach (var child in GetChildren().ToList())
                if (child != DynamicOverlay) Remove(child);

            WingBack = TS1UIGraphics.Get("cpanel/Backgrounds/UniversalBack.TGA");
            BarBack = TS1UIGraphics.Get("cpanel/Backgrounds/PanelBack.bmp");
            ModePatches = new Texture2D[]
            {
                TS1UIGraphics.Get("cpanel/Backgrounds/LivePatch.bmp"),
                TS1UIGraphics.Get("cpanel/Backgrounds/BuyPatch.bmp"),
                TS1UIGraphics.Get("cpanel/Backgrounds/BuildPatch.bmp"),
                TS1UIGraphics.Get("cpanel/Backgrounds/OptionsPatch.bmp"),
                TS1UIGraphics.Get("cpanel/Backgrounds/CameraPatch.bmp"),
            };

            // mode buttons
            LiveButton = Btn("cpanel/Buttons/people.bmp", 10, 6, true);
            BuyButton = Btn("cpanel/Buttons/objects.bmp", 69, 20, true);
            BuildButton = Btn("cpanel/Buttons/arch.bmp", 117, 45, true);
            CameraBtn = Btn("cpanel/Buttons/Camera.bmp", 175, 95, true);
            OptionsButton = Btn("cpanel/Buttons/options.bmp", 193, 125, true);

            // (already in Live mode: nothing to do; Simitone would open its touch-style Sim picker)
            LiveButton.OnButtonClick += (b) => { if (SelectedMode() != UIMainPanelMode.LIVE) OnModeClick?.Invoke(UIMainPanelMode.LIVE); };
            BuyButton.OnButtonClick += (b) => { if (!WatchBlocked()) OnModeClick?.Invoke(UIMainPanelMode.BUY); };
            BuildButton.OnButtonClick += (b) => { if (!WatchBlocked()) OnModeClick?.Invoke(UIMainPanelMode.BUILD); };
            OptionsButton.OnButtonClick += (b) =>
            {
                // touch: Options again closes the open page (the page covers the Options buttons)
                if (Touch && OptionPages != null && OptionPages.Current != UIClassicOptionsPages.Page.None) ShowOptionsPage(UIClassicOptionsPages.Page.None);
                OnModeClick?.Invoke(UIMainPanelMode.OPTIONS);
            };
            CameraBtn.Disabled = true; // camera mode: coming later

            // floors
            FloorUpButton = Btn("cpanel/Buttons/Lev2.bmp", 5, 77);
            FloorDownButton = Btn("cpanel/Buttons/Lev1.bmp", 5, 92);
            FloorUpButton.OnButtonClick += (b) => { if (Screen.Level < 5) Screen.Level++; };
            FloorDownButton.OnButtonClick += (b) => { if (Screen.Level > 1) Screen.Level--; };

            // wall views: roof, walls up, cutaway, walls down
            RoofButton = Btn("cpanel/Buttons/LevRoof.bmp", 32, 78, true);
            WallsUpButton = Btn("cpanel/Buttons/nocut.bmp", 56, 80, true);
            WallsCutButton = Btn("cpanel/Buttons/dynacut.bmp", 80, 85, true);
            WallsDownButton = Btn("cpanel/Buttons/nowall.bmp", 103, 95, true);
            RoofButton.OnButtonClick += (b) => SetCut(3);
            WallsUpButton.OnButtonClick += (b) => SetCut(2);
            WallsCutButton.OnButtonClick += (b) => SetCut(1);
            WallsDownButton.OnButtonClick += (b) => SetCut(0);

            // camera
            ZoomInButton = Btn("cpanel/Buttons/zoomin.bmp", 26, 105);
            ZoomOutButton = Btn("cpanel/Buttons/zoomout.bmp", 26, 149);
            // (the rotate arrows' pictures have a black square behind them: just the arrows)
            RotateCCWButton = Btn("cpanel/Buttons/rotleft.bmp", 43, 127, false, true);
            RotateCWButton = Btn("cpanel/Buttons/rotright.bmp", 9, 127, false, true);
            ZoomInButton.OnButtonClick += (b) => Zoom(-1);
            ZoomOutButton.OnButtonClick += (b) => Zoom(1);
            RotateCWButton.OnButtonClick += (b) => Rotate(1);
            RotateCCWButton.OnButtonClick += (b) => Rotate(-1);

            // speed: Simitone's order is speed 1, 2, 3, pause
            var pause = Btn("cpanel/Buttons/pause.bmp", 85, 135, true);
            pause.SetRow(2, 1); // the red pause row, as in the original
            SpeedButtons = new UIButton[]
            {
                Btn("cpanel/Buttons/Speed1.bmp", 105, 135, true),
                Btn("cpanel/Buttons/Speed2.bmp", 120, 135, true),
                Btn("cpanel/Buttons/Speed3.bmp", 141, 135, true),
                pause
            };
            for (int i = 0; i < 4; i++)
            {
                var speed = i + 1;
                SpeedButtons[i].OnButtonClick += (b) => SwitchSpeed(speed);
            }

            // Options mode: Save, Neighborhood, Quit / Graphics, Sound, Play (positions from the original)
            var save = Btn("cpanel/Buttons/OptSave.bmp", 249, 89);
            var nbhd = Btn("cpanel/Buttons/OptNbhd.bmp", 308, 89);
            var quit = Btn("cpanel/Buttons/OptExit.bmp", 379, 89);
            var graphics = Btn("cpanel/Buttons/optgraphics.bmp", 249, 141);
            var sound = Btn("cpanel/Buttons/optsound.bmp", 309, 141);
            var play = Btn("cpanel/Buttons/optplay.bmp", 371, 141);
            save.OnButtonClick += (b) => Screen.Save();
            nbhd.OnButtonClick += (b) => Screen.ReturnToNeighbourhood(); // asks "save first?" itself
            quit.OnButtonClick += (b) => Screen.CloseAttempt();
            graphics.SelectedFrame = sound.SelectedFrame = play.SelectedFrame = 1;
            graphics.OnButtonClick += (b) => ShowOptionsPage(UIClassicOptionsPages.Page.Graphics);
            sound.OnButtonClick += (b) => ShowOptionsPage(UIClassicOptionsPages.Page.Sound);
            play.OnButtonClick += (b) => ShowOptionsPage(UIClassicOptionsPages.Page.Play);
            GraphicsBtn = graphics; SoundBtn = sound; PlayBtn = play;
            OptionButtons = new UITS1Button[] { save, nbhd, quit, graphics, sound, play };
            OptLineH = TS1UIGraphics.Get("cpanel/Backgrounds/OptionsToothpkHoriz.bmp");
            OptLineV = TS1UIGraphics.Get("cpanel/Backgrounds/OptionsToothpkVert.bmp");

            HelpBtn = Btn("cpanel/Buttons/HelpButton.bmp", 5, 164);
            HelpBtn.OnButtonClick += (b) => UIClassicHelpDialog.Toggle();
            HelpBtn.OnButtonHover += (b) => GameFacade.Cursor.SetCursor(FSO.Common.Rendering.Framework.CursorType.Help);
            HelpBtn.OnButtonExit += (b) => GameFacade.Cursor.SetCursor(FSO.Common.Rendering.Framework.CursorType.Normal);

            // text: clock, friends, money (Simitone's labels are reused for money)
            ClockLabel = Lcd(86, 117, 74, 15, TextAlignment.Center | TextAlignment.Middle, 10);
            FriendsCount = Lcd(84, 159, 30, 17, TextAlignment.Center | TextAlignment.Middle, 11); // family friends (as in the original)
            MoneyLabel = Lcd(112, 159, 82, 17, TextAlignment.Center | TextAlignment.Middle, 11);

            // Simitone's own clock/floor labels stay hidden (the logic still writes to them)
            TimeLabel = Lcd(0, 0, 1, 1, TextAlignment.Left, 10); TimeLabel.Visible = false;
            TimeLabelShadow = Lcd(0, 0, 1, 1, TextAlignment.Left, 10); TimeLabelShadow.Visible = false;
            FloorLabel = Lcd(0, 0, 1, 1, TextAlignment.Left, 10); FloorLabel.Visible = false;
            FloorLabelShadow = Lcd(0, 0, 1, 1, TextAlignment.Left, 10); FloorLabelShadow.Visible = false;
            FriendsLabel = FriendsCount;
            FriendsLabelShadow = Lcd(0, 0, 1, 1, TextAlignment.Left, 10); FriendsLabelShadow.Visible = false;

            // Live mode: mood meter, family faces, Mood/Job/... buttons and pages (drawn every frame)
            LivePanel = new UIClassicLivePanel(screen);
            DynamicOverlay.Add(LivePanel);

            // Buy mode: the original catalog
            BuyPanel = new UIClassicBuyPanel(screen);
            BuyPanel.Visible = false;
            DynamicOverlay.Add(BuyPanel);

            // Build mode: the original tool buttons
            BuildPanel = new UIClassicBuildPanel(screen);
            BuildPanel.Visible = false;
            DynamicOverlay.Add(BuildPanel);

            // Options mode: Graphics / Sound / Play pages
            OptionPages = new UIClassicOptionsPages(screen);
            OptionPages.Visible = false;
            DynamicOverlay.Add(OptionPages);

            // clicks on the bottom bar (and the solid lower part of the wing, between its buttons)
            // shouldn't fall through to the lot
            BarCatcher = ListenForMouse(new Rectangle(0, BarY, BarX + 4000, BarH), (t, s) => { });

            // touch: back from an Options page to the Options buttons
            BackBtn = new UIReduxTextButton("< Back", 64, 24);
            BackBtn.OnButtonClick += (b) => ShowOptionsPage(UIClassicOptionsPages.Page.None);
            BackBtn.Visible = false;
            Add(BackBtn);

            RightBlock = new UIElement[]
            {
                SpeedButtons[3], SpeedButtons[0], SpeedButtons[1], SpeedButtons[2], HelpBtn,
                RotateCCWButton, ZoomInButton, ZoomOutButton, RotateCWButton, FloorUpButton,
                RoofButton, WallsUpButton, WallsCutButton, WallsDownButton, FloorDownButton
            };

            // remember where everything goes on a PC, to put it back when touch is switched off
            foreach (var child in GetChildren())
                if (child != DynamicOverlay && child != BackBtn)
                    ClassicPlace[child] = new Vector4(child.X, child.Y, child.ScaleX, child.ScaleY);

            Size = new Vector2(1024, WingH); // wide enough for the bar contents
            UpdateBuildBuy();
            UpdateZoomButton();
            SetMode(UIMainPanelMode.LIVE);
        }

        private void ShowOptionsPage(UIClassicOptionsPages.Page page)
        {
            // clicking the open page again closes it
            if (OptionPages.Current == page) page = UIClassicOptionsPages.Page.None;
            OptionPages.Show(page);
            GraphicsBtn.Selected = page == UIClassicOptionsPages.Page.Graphics || page == UIClassicOptionsPages.Page.Interface;
            SoundBtn.Selected = page == UIClassicOptionsPages.Page.Sound;
            PlayBtn.Selected = page == UIClassicOptionsPages.Page.Play;
        }

        private UITS1Button Btn(string path, int x, int y, bool cyanWhenSelected = false, bool cutout = false)
        {
            // (cutout: just the button itself - the solid background baked into the picture is taken out)
            var tex = cutout ? TS1UIGraphics.GetCutout(path, 4, 24) : TS1UIGraphics.Get(path);
            var btn = new UITS1Button(tex, 4);
            btn.Position = new Vector2(x, y);
            if (cyanWhenSelected) btn.SelectedFrame = 1;
            Add(btn);
            return btn;
        }

        private UILabel Lcd(int x, int y, int w, int h, TextAlignment align, int size)
        {
            var label = new UILabel();
            label.Position = new Vector2(x, y);
            label.Size = new Vector2(w, h);
            label.Alignment = align;
            label.CaptionStyle = TextStyle.DefaultLabel.Clone();
            label.CaptionStyle.Size = size;
            label.CaptionStyle.Color = LcdGreen;
            label.Caption = "";
            Add(label);
            return label;
        }

        private FSO.LotView.Utils.Camera.ICameraController ActiveCamera
        {
            get { return Screen.vm?.Context?.World?.State?.Cameras?.ActiveCamera; }
        }

        private void Zoom(int dir)
        {
            if (ActiveCamera?.UseZoomHold != false && Screen.InLot) return; // 3D camera zooms while held
            Screen.ZoomLevel = Screen.ZoomLevel + dir;
            UpdateZoomButton();
        }

        private void Rotate(int dir)
        {
            if (ActiveCamera?.UseRotateHold != false && Screen.InLot) return;
            Screen.Rotation = (Screen.Rotation + dir + 4) % 4;
        }

        // Simitone calls the base SetMode (it marks the selected mode button), so read it back from there
        private UIMainPanelMode SelectedMode()
        {
            if (BuyButton.Selected) return UIMainPanelMode.BUY;
            if (BuildButton.Selected) return UIMainPanelMode.BUILD;
            if (OptionsButton.Selected) return UIMainPanelMode.OPTIONS;
            return UIMainPanelMode.LIVE;
        }

        private void Reposition()
        {
            int W = GlobalSettings.Default.GraphicsWidth, H = GlobalSettings.Default.GraphicsHeight;
            var want = !ReduxTouch.ReduxLayout ? PanelLayout.Classic
                : (H > W ? PanelLayout.TouchTall : PanelLayout.TouchWide);
            float s;
            // touch, sideways: the panel (with the strip of faces and tabs above it) takes a bit under half
            //   the screen's height, but the screen is always at least 620 panel units wide
            // touch, upright: the screen is 400 units wide (the panel is in two rows)
            if (want == PanelLayout.TouchWide) s = Math.Max(0.5f, Math.Min(0.46f * H / 128f, W / 620f));
            else if (want == PanelLayout.TouchTall) s = Math.Max(0.5f, Math.Min(W / 400f, 0.42f * H / 226f));
            else s = ReduxTheme.PanelScale(H);
            UIScale = s;
            ScaleX = ScaleY = s;
            float wu = W / s;
            if (want != Layout || Math.Abs(wu - LayoutWu) > 0.5f) ApplyLayout(want, wu);
            float bottom = want == PanelLayout.TouchTall ? 281 : WingH;
            Position = new Vector2(0, H - bottom * s);
            if (want == PanelLayout.Classic)
            {
                ContentX = BarX;
                BarRight = wu;
                PanelTopScreen = Position.Y;
                BarLeftScreen = Position.X + (WingW + 6) * s;
            }
            else
            {
                PanelTopScreen = Position.Y + 55 * s;
                BarLeftScreen = Position.X + ContentX * s;
            }
            BarTopScreen = Position.Y + BarY * s;
        }

        /// <summary>Scales a Sims 1 art button to fit a box, keeping its shape, centred in the box.</summary>
        public static void FitButton(UIButton b, float x, float y, float w, float h)
        {
            var t = b.Texture;
            int frames = (b as UITS1Button)?.Frames ?? 4;
            int rows = (b as UITS1Button)?.Rows ?? 1;
            if (t == null || t.Width < frames * 2 || t.Height < rows * 2)
            {
                b.ScaleX = b.ScaleY = 1;
                b.Position = new Vector2(x, y);
                return;
            }
            float fw = t.Width / (float)frames, fh = t.Height / (float)rows;
            float k = Math.Min(w / fw, h / fh);
            b.ScaleX = b.ScaleY = k;
            b.Position = new Vector2(x + (w - fw * k) / 2f, y + (h - fh * k) / 2f);
        }

        /// <summary>
        /// The right edge of the bar's contents in an element's own units (it may be moved or scaled
        /// inside the panel). Used by the catalogs to work out how many pictures fit.
        /// </summary>
        public static float RightEdge(UIElement e)
        {
            if (e == null) return GlobalSettings.Default.GraphicsWidth;
            var ucp = e as UIClassicUCP;
            if (ucp != null) return ucp.BarRight;
            float sx = e.ScaleX == 0 ? 1 : e.ScaleX;
            return (RightEdge(e.Parent) - e.X) / sx;
        }

        private void ApplyLayout(PanelLayout want, float wu)
        {
            Layout = want;
            LayoutWu = wu;
            foreach (var pair in ClassicPlace)
            {
                pair.Key.Position = new Vector2(pair.Value.X, pair.Value.Y);
                pair.Key.ScaleX = pair.Value.Z;
                pair.Key.ScaleY = pair.Value.W;
            }
            CameraBtn.Visible = want == PanelLayout.Classic;
            FriendsCount.Visible = want == PanelLayout.Classic;
            ClockLabel.CaptionStyle.Size = want == PanelLayout.Classic ? 10 : 12;
            MoneyLabel.CaptionStyle.Size = want == PanelLayout.Classic ? 11 : 12;

            if (want == PanelLayout.Classic)
            {
                BackBtn.Visible = false;
                ClockLabel.Size = new Vector2(74, 15);
                MoneyLabel.Size = new Vector2(82, 17);
                Size = new Vector2(1024, WingH);
                BarCatcher.Region = new Rectangle(0, BarY, BarX + 4000, BarH);
                Invalidate();
                return;
            }

            bool tall = want == PanelLayout.TouchTall;
            ContentX = tall ? 4 : 100;
            BarRight = tall ? wu - 4 : wu - 136;

            // Live / Buy / Build / Options: a diamond of big buttons
            float cy = tall ? 233 : 133;
            DiamondC = new Vector2(48, cy);
            FitButton(LiveButton, 33, cy - 46, 30, 30);
            FitButton(BuyButton, 2, cy - 15, 30, 30);
            FitButton(BuildButton, 64, cy - 15, 30, 30);
            FitButton(OptionsButton, 33, cy + 16, 30, 30);

            // speed, camera, walls and floors: a block of buttons at the right
            RX = wu - 132;
            RY = tall ? 187 : 87;
            for (int i = 0; i < RightBlock.Length; i++)
                FitButton((UIButton)RightBlock[i], RX + (i % 5) * 26, RY + (i / 5) * 32, 24, 24);

            // clock and money: above the block (sideways), or between the diamond and the block (upright)
            if (tall)
            {
                // (as tall as the button block beside it: down to the bottom of the panel)
                ClockBox = new Rectangle(100, (int)RY - 4, (int)(RX - 108), 96);
                ClockLabel.Position = new Vector2(104, RY + 16); ClockLabel.Size = new Vector2(ClockBox.Width - 8, 24);
                MoneyLabel.Position = new Vector2(104, RY + 46); MoneyLabel.Size = new Vector2(ClockBox.Width - 8, 24);
                ClockLabel.CaptionStyle.Size = MoneyLabel.CaptionStyle.Size = 14;
            }
            else
            {
                ClockBox = new Rectangle((int)RX - 3, 55, 134, 26);
                ClockLabel.Position = new Vector2(RX + 1, 57); ClockLabel.Size = new Vector2(60, 22);
                MoneyLabel.Position = new Vector2(RX + 61, 57); MoneyLabel.Size = new Vector2(66, 22);
            }

            // Options: Save / Neighborhood / Quit, Graphics / Sound / Play, straight after the diamond
            float dx = ContentX + 4 - 249;
            foreach (var b in OptionButtons)
            {
                var v = ClassicPlace[b];
                b.Position = new Vector2(v.X + dx, v.Y);
            }
            BackBtn.Position = new Vector2(ContentX, 56);

            Size = new Vector2(wu + 8, tall ? 284 : WingH);
            BarCatcher.Region = new Rectangle(0, BarY, (int)wu + 8, tall ? 200 : BarH);
            Invalidate();
        }

        /// <summary>Touch: puts the bar's panels where the touch layout wants them.</summary>
        private void PlaceBarPanels(bool options)
        {
            float panelX = Touch ? ContentX - 232 : 0; // (the Buy and Build bars start at 232 on a PC)
            if (BuyPanel.X != panelX) BuyPanel.X = panelX;
            if (BuildPanel.X != panelX) BuildPanel.X = panelX;
            LivePanel.SetTouch(Touch, ContentX, BarRight);

            bool pageOpen = OptionPages.Current != UIClassicOptionsPages.Page.None;
            if (Touch && options && pageOpen)
            {
                // the page takes the whole bar (the speed block makes way), shrunk if it doesn't fit
                float avail = (LayoutWu - 4) - ContentX;
                float f = Math.Min(1f, avail / (930f - 452f));
                OptionPages.ScaleX = OptionPages.ScaleY = f;
                OptionPages.Position = new Vector2(ContentX - 452 * f, BarY - BarY * f);
            }
            else if (OptionPages.ScaleX != 1 || OptionPages.X != 0 || OptionPages.Y != 0)
            {
                OptionPages.ScaleX = OptionPages.ScaleY = 1;
                OptionPages.Position = Vector2.Zero;
            }

            bool hideForPage = Touch && options && pageOpen;
            if (BackBtn.Visible != hideForPage) { BackBtn.Visible = hideForPage; Invalidate(); }
            bool showBlock = !hideForPage;
            if (showBlock != RightBlockShown)
            {
                RightBlockShown = showBlock;
                foreach (var e in RightBlock) e.Visible = showBlock;
                Invalidate();
            }
        }

        private string LastClockText = "";
        private int LastFriends = -1;
        public override void Update(UpdateState state)
        {
            Reposition();
            base.Update(state);
            CurrentMode = SelectedMode();
            // (F1 is Live mode, as in the original game; Help is the ? button)

            // bar contents that are already classic: show ours, hide Simitone's
            bool options = CurrentMode == UIMainPanelMode.OPTIONS;
            // (touch: an open Options page covers the Options buttons)
            bool optPage = Touch && OptionPages.Current != UIClassicOptionsPages.Page.None;
            foreach (var b in OptionButtons) b.Visible = options && !optPage;
            bool live = CurrentMode == UIMainPanelMode.LIVE;
            bool buy = CurrentMode == UIMainPanelMode.BUY;
            bool build = CurrentMode == UIMainPanelMode.BUILD;
            LivePanel.Visible = live;
            BuyPanel.Visible = buy;
            BuildPanel.Visible = build;
            OptionPages.Visible = options;
            if (!options && OptionPages.Current != UIClassicOptionsPages.Page.None) ShowOptionsPage(UIClassicOptionsPages.Page.None);
            PlaceBarPanels(options);
            if (ModeContents != null)
            {
                ModeContents.ClassicHidden = options || live || buy || build;
                if (ModeContents.ClassicHidden) ModeContents.Visible = false;
            }

            var vm = Screen.vm;
            if (vm != null)
            {
                int hour = vm.Context.Clock.Hours, min = vm.Context.Clock.Minutes;
                var suffix = (hour > 11) ? "PM" : "AM";
                hour %= 12; if (hour == 0) hour = 12;
                var text = suffix + " " + hour + ":" + min.ToString().PadLeft(2, '0');
                if (text != LastClockText) { LastClockText = text; ClockLabel.Caption = text; }
            }

            var friends = Screen.ActiveFamily?.FamilyFriends ?? 0;
            if (friends != LastFriends) { LastFriends = friends; FriendsCount.Caption = friends.ToString(); }
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            if (Touch) { DrawTouch(batch); base.Draw(batch); return; }
            float screenRight = (GlobalSettings.Default.GraphicsWidth / UIScale) + 4;
            bool redux = ReduxTheme.Redux;
            if (redux)
            {
                // Redux: one smooth bar in the UI colour, running behind the wing to the screen edge
                var px = TextureGenerator.GetPxWhite(GameFacade.GraphicsDevice);
                DrawLocalTexture(batch, px, null, new Vector2(0, BarY - 4), new Vector2(screenRight, 4), Color.Black * 0.22f);
                DrawLocalTexture(batch, px, null, new Vector2(0, BarY - 1), new Vector2(screenRight, 1), ReduxTheme.Edge);
                DrawLocalTexture(batch, ReduxTheme.BarGradient(BarH), null, new Vector2(0, BarY), new Vector2(screenRight, 1));
            }
            else
            {
                // bottom bar: draw the original 804px bar, then stretch its right edge to the screen edge
                DrawLocalTexture(batch, BarBack, new Vector2(BarX, BarY));
                float extra = screenRight - (BarX + BarBack.Width);
                if (extra > 0 && BarBack.Width > 8) // (not with the 1x1 stand-in for missing art)
                    DrawLocalTexture(batch, BarBack, new Rectangle(BarBack.Width - 8, 0, 4, BarBack.Height),
                        new Vector2(BarX + BarBack.Width - 4, BarY), new Vector2((extra + 4) / 4f, 1));
            }

            // the wing, then the white arc that points at the current mode (Redux: recoloured to the UI colour)
            DrawLocalTexture(batch, redux ? ReduxTheme.Tinted(WingBack) : WingBack, Vector2.Zero);
            var patch = ModePatches[ModeIndex(CurrentMode)];
            if (patch.Width > 1)
                DrawLocalTexture(batch, redux ? ReduxTheme.Tinted(patch) : patch, new Vector2(WingW - patch.Width, WingH - patch.Height));

            if (CurrentMode == UIMainPanelMode.OPTIONS)
            {
                DrawLocalTexture(batch, OptLineH, new Vector2(236, 134));
                DrawLocalTexture(batch, OptLineV, new Vector2(442, 89));
            }

            base.Draw(batch);
        }

        /// <summary>Touch: one bar across the bottom, wells behind the diamond, the speed block and the clock.</summary>
        private void DrawTouch(UISpriteBatch batch)
        {
            var px = TextureGenerator.GetPxWhite(GameFacade.GraphicsDevice);
            float w = LayoutWu + 8;
            bool tall = Layout == PanelLayout.TouchTall;
            int h = (tall ? 284 : WingH) - BarY;
            DrawLocalTexture(batch, px, null, new Vector2(0, BarY - 4), new Vector2(w, 4), Color.Black * 0.22f);
            DrawLocalTexture(batch, px, null, new Vector2(0, BarY - 1), new Vector2(w, 1), ReduxTheme.Edge);
            DrawLocalTexture(batch, ReduxTheme.BarGradient(h), null, new Vector2(0, BarY), new Vector2(w, 1));
            if (tall) DrawLocalTexture(batch, px, null, new Vector2(4, 184), new Vector2(LayoutWu - 8, 1), ReduxTheme.Edge * 0.6f);

            DrawLocalTexture(batch, ReduxTheme.Box(72, 72, 36, ReduxTheme.Deeper, ReduxTheme.Edge, ReduxTheme.Rim * 0.6f, 1, true), DiamondC - new Vector2(36, 36));
            if (RightBlockShown)
                DrawLocalTexture(batch, ReduxTheme.Box(134, 96, 10, ReduxTheme.Deeper * 0.8f, ReduxTheme.Edge * 0.8f, Color.Transparent, 0, true), new Vector2(RX - 3, RY - 4));
            DrawLocalTexture(batch, ReduxTheme.Box(ClockBox.Width, ClockBox.Height, 8, ReduxTheme.Deeper, ReduxTheme.Edge, ReduxTheme.Rim * 0.5f, 1, true), new Vector2(ClockBox.X, ClockBox.Y));
        }

        private int ModeIndex(UIMainPanelMode mode)
        {
            switch (mode)
            {
                case UIMainPanelMode.BUY: return 1;
                case UIMainPanelMode.BUILD: return 2;
                case UIMainPanelMode.OPTIONS: return 3;
                default: return 0;
            }
        }
    
        /// <summary>SimsRedux: no buying or building while just watching a background family.</summary>
        private bool WatchBlocked()
        {
            if ((FSO.Client.UI.Framework.UIScreen.Current as Simitone.Client.UI.Screens.TS1GameScreen)?.Watching != true) return false;
            UIClassicDialog.Ok("Just Watching", "This isn't your family's home. Take the family over from the neighborhood to buy and build here.");
            return true;
        }
}
}
