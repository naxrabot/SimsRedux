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

        public static SimitoneGame CreateGame(int width, int height)
        {
            FSOFileSystem.Current = new BrowserFileSystem(); // the player's files live in the browser (js/simsfs.js)
            FSOEnvironment.NoThreads = true;      // browsers can't run the game's loading threads
            FSOEnvironment.GameThread = System.Threading.Thread.CurrentThread; // the browser's one thread is the game thread
            FSO.Common.Utils.GameThread.Game = System.Threading.Thread.CurrentThread;
            FSO.Files.ImageLoaderHelpers.BitmapFunction = WebImageDecoder.Decode; // pictures are decoded in C# here
            FSO.Common.Utils.TextureLoad.Decoder = WebImageDecoder.Decode;
            FSOEnvironment.DirectX = false;
            FSOEnvironment.Linux = false;
            // WebGL is like the phone (iOS) build: GL ES style shaders, depth done in software
            FSOEnvironment.GLVer = 2;
            FSOEnvironment.SoftwareDepth = true;
            FSOEnvironment.UseMRT = false;        // WebGL has limited multiple-render-target support
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

            return new SimitoneGame();
        }
    }
}
