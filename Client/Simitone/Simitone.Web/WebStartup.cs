using FSO.Client;
using FSO.Common;
using FSO.Common.IO;
using FSO.LotView;
using Simitone.Client;

namespace Simitone.Web
{
    /// <summary>
    /// SimsRedux web: the browser's version of the Windows launcher (Program.cs / GameStartProxy).
    /// Sets up where the game looks for things, turns off what browsers can't do, and makes the game.
    /// </summary>
    public static class WebStartup
    {
        /// <summary>Where the player's own Sims 1 files appear once they've chosen their game folder (stage 3).</summary>
        public const string GamePath = "/sims/";

        public static SimitoneGame CreateGame(int width, int height, string gfxMode)
        {
            WebPlatform.Install();                // browser-only options: change Sims folder, full screen
            FSOFileSystem.Current = new BrowserFileSystem(); // the player's files live in the browser (js/simsfs.js)
            FSOEnvironment.NoThreads = true;      // browsers can't run the game's loading threads
            FSOEnvironment.GameThread = System.Threading.Thread.CurrentThread; // the browser's one thread is the game thread
            FSO.Common.Utils.GameThread.Game = System.Threading.Thread.CurrentThread;
            FSO.Files.ImageLoaderHelpers.BitmapFunction = WebImageDecoder.Decode; // pictures are decoded in C# here
            FSO.Common.Utils.TextureLoad.Decoder = WebImageDecoder.Decode;
            FSOEnvironment.DirectX = false;
            FSOEnvironment.Linux = false;
            // Graphics mode (choose with ?gfx=... in the address while we find the best one):
            //   mobile  - like the phone (iOS) build: simple shaders, depth worked out in software
            //   desktop - like the PC build: real depth, two outputs at once (WebGL 2 can do this)
            //   nomrt   - like the PC build but drawing depth in a separate pass (the default: looks right)
            Console.WriteLine("[SimsRedux] graphics mode: " + gfxMode);
            if (gfxMode == "desktop" || gfxMode == "nomrt")
            {
                FSOEnvironment.GLVer = 3;
                FSOEnvironment.SoftwareDepth = false;
                FSOEnvironment.UseMRT = gfxMode == "desktop";
            }
            else
            {
                FSOEnvironment.GLVer = 2;
                FSOEnvironment.SoftwareDepth = true;
                FSOEnvironment.UseMRT = false;
            }
            // Sims: the desktop Sim shader draws nothing in browsers, the phone one works (with real depth too)
            FSO.LotView.WorldContent.SimpleAvatars = true;
            FSOEnvironment.TexCompress = false;
            FSOEnvironment.TexCompressSupport = false;
            FSOEnvironment.MSAASupport = false;
            FSOEnvironment.EnableNPOTMip = true;
            FSOEnvironment.ContentDir = "Content/";
            FSOEnvironment.GFXContentDir = "Content/Web"; // built shaders + OpenGL fonts/textures (wwwroot/Content/Web)
            FSOEnvironment.UserDir = "/userdata/";
            FSOEnvironment.Args = "-classic";

            GameFacade.DirectX = false;
            World.DirectX = false;

            GlobalSettings.Default.StartupPath = GamePath;
            GlobalSettings.Default.TS1HybridEnable = true;
            GlobalSettings.Default.TS1HybridPath = GamePath;
            GlobalSettings.Default.ClientVersion = "0";
            GlobalSettings.Default.LightingMode = 0;
            GlobalSettings.Default.AntiAlias = 0;
            GlobalSettings.Default.ComplexShaders = false;
            // the game fills the browser window (and follows it when the window is resized),
            // so the mouse lines up with what's drawn
            GlobalSettings.Default.Windowed = true;
            if (width > 0 && height > 0)
            {
                GlobalSettings.Default.GraphicsWidth = width;
                GlobalSettings.Default.GraphicsHeight = height;
            }
            GlobalSettings.Default.EnableTransitions = true;

            try { var smarts = Simitone.Client.Utils.ReduxSettings.Autonomy; } catch { } // the Sim Smarts setting
            return new SimitoneGame();
        }
    }
}
