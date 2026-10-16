using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FSO.Client.UI.Framework;
using FSO.Client.UI.Controls;
using System.Threading;
using FSO.Client;
using FSO.Common.Rendering.Framework.Model;
using FSO.Content;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FSO.Common.Utils;
using FSO.SimAntics;

using FSO.Common.IO;
namespace Simitone.Client.UI.Screens
{
    public class LoadingScreen : FSO.Client.UI.Framework.GameScreen
    {
        public string[] LoadText = new string[] {
            "", //Started = 0,
            "Identifying Works...", //ScanningFiles = 1,
            "Categorising Works...", //InitGlobal = 2,
            "Arranging Wardrobes...", //InitBCF = 3,
            "Reticulating Splines...", //InitAvatars = 4,
            "Performing Sound Check...", //InitAudio = 5,
            "Arranging Furniture...", //InitObjects = 6,
            "Building Concert Hall...", //InitArch = 7,
            " " //Done = 8,
        };

        public UISimitoneBg Bg;
        public UIDiagonalStripe ProgressDiag;
        public UIDiagonalStripe TextDiag;
        public UILoadProgress LoadProgress;
        public UISimitoneLogo Logo;
        public UISimitoneLoadLabel LastLabel;
        public bool LoadingComplete;
        private bool EnteredGame;
        public UITweenInstance LogoTween;

        private bool Closing = false;
        /// <summary>SimsRedux: the loading text sits near the bottom, under the wallpaper.</summary>
        private const float LabelY = 0.9f;
        private UIReduxProgressBar ReduxBar;
        private float _i;
        public float InterpolatedAnimation
        {
            set
            {
                TextDiag.X = (Closing) ? 0 : (ScreenWidth * (1 - value));
                TextDiag.BodySize = new Point((int)(value * ScreenWidth), 75);
                TextDiag.Y = ScreenHeight*LabelY - 37;

                ProgressDiag.X = (Closing) ? (ScreenWidth * (1 - value)) : 0;
                ProgressDiag.BodySize = new Point((int)(value * ScreenWidth), 150);

                LoadProgress.Opacity = value;

                _i = value;
            }

            get
            {
                return _i;
            }
        }

        public LoadingScreen()
        {
            Content.InitBasic(GlobalSettings.Default.StartupPath, GameFacade.GraphicsDevice);

            Bg = new UISimitoneBg();
            Bg.Position = (new Vector2(ScreenWidth, ScreenHeight)) / 2;
            Add(Bg);

            ProgressDiag = new UIDiagonalStripe(new Point(0, 150), UIDiagonalStripeSide.RIGHT, Color.Black * 0.75f);
            ProgressDiag.Position = new Vector2(0, ScreenHeight / 2 - 75);
            Add(ProgressDiag);

            TextDiag = new UIDiagonalStripe(new Point(0, 150), UIDiagonalStripeSide.LEFT, Color.Black * 0.5f);
            TextDiag.Position = new Vector2(0, ScreenHeight * LabelY - 37);
            Add(TextDiag);

            GameFacade.Screens.Tween.To(this, 0.5f, new Dictionary<string, float>() { { "InterpolatedAnimation", 1f } }, TweenQuad.EaseOut);

            LoadProgress = new UILoadProgress();
            LoadProgress.Position = (new Vector2(ScreenWidth, ScreenHeight) - new Vector2(1012, 128))/2;
            Add(LoadProgress);

            Logo = new UISimitoneLogo();
            Logo.Position = new Vector2(ScreenWidth, ScreenHeight) / 2;
            Add(Logo);
            LogoTween = GameFacade.Screens.Tween.To(Logo, 1f, new Dictionary<string, float>() { { "Y", ScreenHeight/4 }, { "ScaleX", 0.5f }, { "ScaleY", 0.5f } }, TweenQuad.EaseOut);

            // SimsRedux: The Sims Redux wallpaper instead of Simitone's logo and music-staff progress bar.
            // (Those two ignore Visible, so take them off the screen completely.)
            Remove(Logo);
            Remove(LoadProgress);
            Remove(ProgressDiag);
            Remove(TextDiag);
            ReduxBar = new UIReduxProgressBar();
            Add(ReduxBar);

            InterpolatedAnimation = InterpolatedAnimation;

            FSO.Common.Utils.FSOThreads.Start(() => {
                VMContext.InitVMConfig(true);
                FSO.Content.Content.ReduxCreatorContent = Simitone.Client.Utils.ReduxCreator.ShouldLoad(); // SimsRedux: creator items (Test server)
                FSO.Content.Content.Init(GlobalSettings.Default.StartupPath, GameFacade.GraphicsDevice);
                lock (this)
                {
                    LoadingComplete = true;
                }
            });
        }

        public void Close()
        {
            Closing = true;
            GameThread.SetTimeout(() =>
            {
                ProgressDiag.DiagSide = UIDiagonalStripeSide.LEFT;
                TextDiag.DiagSide = UIDiagonalStripeSide.RIGHT;
                GameFacade.Screens.Tween.To(this, 0.5f, new Dictionary<string, float>() { { "InterpolatedAnimation", 0f } }, TweenQuad.EaseOut);
                GameFacade.Screens.Tween.To(Logo, 0.5f, new Dictionary<string, float>() { { "Y", -200f } }, TweenQuad.EaseIn);
                GameFacade.Screens.Tween.To(Bg, 0.5f, new Dictionary<string, float>() { { "Opacity", 0f } }, TweenQuad.EaseOut);

                GameThread.SetTimeout(() =>
                {
                    Remove(ProgressDiag);
                    Remove(TextDiag);
                    Remove(Logo);
                    Remove(LoadProgress);
                    if (ReduxBar != null) ReduxBar.Parent.Remove(ReduxBar);
                    Bg.Parent.Remove(Bg);
                }, 500);
            }, 750);
        }

        public override void GameResized()
        {
            base.GameResized();
            GameFacade.Screens.Tween.Stop(LogoTween, true);
            Bg.Position = (new Vector2(ScreenWidth, ScreenHeight)) / 2;
            ProgressDiag.Position = new Vector2(0, ScreenHeight / 2 - 75);
            TextDiag.Position = new Vector2(0, ScreenHeight * LabelY - 37);
            LoadProgress.Position = (new Vector2(ScreenWidth, ScreenHeight) - new Vector2(1012, 128)) / 2;
            Logo.Position = new Vector2(ScreenWidth, ScreenHeight/2) / 2;
            if (LastLabel != null) LastLabel.Position = new Vector2(ScreenWidth / 2, ScreenHeight * LabelY);

            InterpolatedAnimation = InterpolatedAnimation;
        }

        public ContentLoadingProgress LastProgress = ContentLoadingProgress.Invalid;

        public override void Update(UpdateState state)
        {
            if (LastProgress != Content.LoadProgress)
            {
                LastProgress = Content.LoadProgress;

                if (LastLabel != null)
                {
                    LastLabel.Kill();
                }
                LastLabel = new UISimitoneLoadLabel(LoadText[(int)LastProgress]);
                LastLabel.Position = new Vector2(ScreenWidth/2, ScreenHeight * LabelY);
                Add(LastLabel);

                LoadProgress.OverallPercent = (float)LastProgress / (float)ContentLoadingProgress.Done;
                if (ReduxBar != null) ReduxBar.Target = LoadProgress.OverallPercent;
            }
            lock (this)
            {
                // (once: EnterGameMode waits for the next update, and asking again before then
                //  could build a second game screen)
                if (LoadingComplete && !EnteredGame)
                {
                    EnteredGame = true;
                    GameController.EnterGameMode("", false);
                }
            }
            base.Update(state);
        }
    }

    public class UISimitoneLogo : UIElement
    {
        public Texture2D Logo;

        public UISimitoneLogo() : base()
        {
            var ui = Content.Get().CustomUI;
            Logo = ui.Get("load_logo.png").Get(GameFacade.GraphicsDevice);
        }

        public override void Draw(UISpriteBatch batch)
        {
            DrawLocalTexture(batch, Logo, new Vector2(Logo.Width, Logo.Height) / -2);
        }
    }

    public class UISimitoneBg : UIElement
    {
        public Texture2D Bg;
        // SimsRedux: The Sims Redux wallpaper (Content/redux/loading_wallpaper.jpg)
        private static Texture2D Wallpaper;
        private static bool WallpaperTried;
        private static readonly Color WallpaperBlue = new Color(0, 43, 135); // the wallpaper's own background colour

        /// <summary>SimsRedux: the wallpaper for other loading screens (null if it can't be shown).</summary>
        public static Texture2D SharedWallpaper()
        {
            LoadWallpaper();
            return Wallpaper;
        }
        public static Color SharedWallpaperBlue => WallpaperBlue;

        public UISimitoneBg() : base()
        {
            var ui = Content.Get().CustomUI;
            Bg = ui.Get("load_static_bg.png").Get(GameFacade.GraphicsDevice);
            LoadWallpaper();
        }

        private static void LoadWallpaper()
        {
            if (!WallpaperTried)
            {
                WallpaperTried = true;
                try
                {
                    var path = System.IO.Path.Combine(FSO.Common.FSOEnvironment.ContentDir, "redux/loading_wallpaper.jpg");
                    if (FSOFile.Exists(path))
                        using (var stream = FSOFile.OpenRead(path))
                            Wallpaper = FSO.Common.Utils.TextureLoad.FromStream(GameFacade.GraphicsDevice, stream);
                }
                catch (Exception e)
                {
                    Console.WriteLine("[SimsRedux] Couldn't load the loading wallpaper: " + e.Message);
                }
            }
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            float sw = GameFacade.Screens.CurrentUIScreen.ScreenWidth, sh = GameFacade.Screens.CurrentUIScreen.ScreenHeight;
            if (Wallpaper != null)
            {
                // whole picture visible, centred, blue fills the rest of the screen
                var px = TextureGenerator.GetPxWhite(batch.GraphicsDevice);
                DrawLocalTexture(batch, px, null, new Vector2(-sw, -sh), new Vector2(sw * 2, sh * 2), WallpaperBlue * Opacity);
                var wscale = Math.Min(sw / Wallpaper.Width, sh / Wallpaper.Height);
                DrawLocalTexture(batch, Wallpaper, null, new Vector2(Wallpaper.Width, Wallpaper.Height) / -2 * wscale, new Vector2(wscale), Color.White * Opacity);
                return;
            }
            // SimsRedux: never Simitone's logo screen - plain Sims Redux blue if the wallpaper can't be shown
            var blank = TextureGenerator.GetPxWhite(batch.GraphicsDevice);
            DrawLocalTexture(batch, blank, null, new Vector2(-sw, -sh), new Vector2(sw * 2, sh * 2), WallpaperBlue * Opacity);
        }
    }

    /// <summary>SimsRedux: a slim classic progress bar under the loading wallpaper.</summary>
    public class UIReduxProgressBar : UIElement
    {
        public float Target;
        private float Shown;

        public override void Update(UpdateState state)
        {
            base.Update(state);
            Shown += (Target - Shown) * 0.15f;
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            float sw = GameFacade.Screens.CurrentUIScreen.ScreenWidth, sh = GameFacade.Screens.CurrentUIScreen.ScreenHeight;
            var px = TextureGenerator.GetPxWhite(batch.GraphicsDevice);
            float w = Math.Min(460, sw * 0.6f), h = 10;
            var pos = new Vector2((sw - w) / 2, sh * 0.84f);
            DrawLocalTexture(batch, px, null, pos - new Vector2(2, 2), new Vector2(w + 4, h + 4), new Color(150, 170, 230));
            DrawLocalTexture(batch, px, null, pos, new Vector2(w, h), new Color(0, 20, 80));
            DrawLocalTexture(batch, px, null, pos, new Vector2(w * Math.Max(0, Math.Min(1, Shown)), h), new Color(240, 220, 110)); // the "REDUX!" yellow
        }
    }

    public class UISimitoneLoadLabel : UIContainer
    {
        public UILabel Label;

        private float _Alpha;
        public float Alpha
        {
            get
            {
                return _Alpha;
            }
            set
            {
                Label.CaptionStyle.Color = Color.White * value;
                _Alpha = value;
            }
        }

        public UISimitoneLoadLabel(string text)
        {
            Label = new UILabel();
            Label.CaptionStyle = Label.CaptionStyle.Clone();
            Label.CaptionStyle.Size = 37;
            Label.CaptionStyle.Color = Color.White;
            Label.Caption = text;
            Label.Alignment = TextAlignment.Center | TextAlignment.Middle;
            Label.Size = new Vector2(1, 1);
            Label.Y = 100f;
            Add(Label);
            Alpha = Alpha;
            GameFacade.Screens.Tween.To(Label, 0.4f, new Dictionary<string, float>() { { "Y", 0f } }, TweenQuad.EaseOut);
            GameFacade.Screens.Tween.To(this, 0.4f, new Dictionary<string, float>() { { "Alpha", 1f } }, TweenQuad.EaseOut);
        }

        public void Kill()
        {
            GameFacade.Screens.Tween.To(Label, 0.4f, new Dictionary<string, float>() { { "Y", -100f } }, TweenQuad.EaseIn);
            GameFacade.Screens.Tween.To(this, 0.4f, new Dictionary<string, float>() { { "Alpha", 0f } }, TweenQuad.EaseIn);
            GameThread.SetTimeout(() => { Parent.Remove(this); }, 400);
        }
    }
}
