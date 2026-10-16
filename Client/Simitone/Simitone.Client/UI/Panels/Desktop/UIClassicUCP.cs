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
            OptionsButton.OnButtonClick += (b) => OnModeClick?.Invoke(UIMainPanelMode.OPTIONS);
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
            RotateCCWButton = Btn("cpanel/Buttons/rotleft.bmp", 43, 127);
            RotateCWButton = Btn("cpanel/Buttons/rotright.bmp", 9, 127);
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
            ListenForMouse(new Rectangle(0, BarY, BarX + 4000, BarH), (t, s) => { });

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

        private UITS1Button Btn(string path, int x, int y, bool cyanWhenSelected = false)
        {
            var tex = TS1UIGraphics.Get(path);
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
            UIScale = ReduxTheme.PanelScale(GlobalSettings.Default.GraphicsHeight);
            ScaleX = ScaleY = UIScale;
            Position = new Vector2(0, GlobalSettings.Default.GraphicsHeight - WingH * UIScale);
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
            foreach (var b in OptionButtons) b.Visible = options;
            bool live = CurrentMode == UIMainPanelMode.LIVE;
            bool buy = CurrentMode == UIMainPanelMode.BUY;
            bool build = CurrentMode == UIMainPanelMode.BUILD;
            LivePanel.Visible = live;
            BuyPanel.Visible = buy;
            BuildPanel.Visible = build;
            OptionPages.Visible = options;
            if (!options && OptionPages.Current != UIClassicOptionsPages.Page.None) ShowOptionsPage(UIClassicOptionsPages.Page.None);
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
