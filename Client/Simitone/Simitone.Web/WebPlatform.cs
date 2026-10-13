using Simitone.Client.Utils;
using System.Runtime.InteropServices.JavaScript;

namespace Simitone.Web
{
    /// <summary>
    /// SimsRedux web: the browser-only features the game's Options offer (see ReduxPlatform):
    /// choosing a different Sims folder and the browser's full screen. Answered by js/simsfs.js.
    /// </summary>
    public static partial class WebPlatform
    {
        [JSImport("globalThis.SimsFS.changeFolder")] internal static partial void JsChangeFolder();
        [JSImport("globalThis.SimsFS.setFullScreen")] internal static partial void JsSetFullScreen(bool on);
        [JSImport("globalThis.SimsFS.isFullScreen")] internal static partial bool JsIsFullScreen();

        public static void Install()
        {
            ReduxPlatform.ChangeGameFolder = JsChangeFolder;
            ReduxPlatform.SetFullScreen = JsSetFullScreen;
            ReduxPlatform.IsFullScreen = JsIsFullScreen;
        }
    }
}
