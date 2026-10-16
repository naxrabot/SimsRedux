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
        [JSImport("globalThis.SimsFS.accountServer")] internal static partial string JsAccountServer();
        [JSImport("globalThis.SimsFS.accountToken")] internal static partial string JsAccountToken();
        [JSImport("globalThis.SimsFS.accountName")] internal static partial string JsAccountName();
        [JSImport("globalThis.SimsFS.onlineBlockedReason")] internal static partial string JsOnlineBlockedReason();

        public static void Install()
        {
            ReduxPlatform.CanHost = false;      // browsers can't accept incoming connections
            ReduxPlatform.ChangeGameFolder = JsChangeFolder;
            ReduxPlatform.SetFullScreen = JsSetFullScreen;
            ReduxPlatform.IsFullScreen = JsIsFullScreen;
            ReduxPlatform.AccountServer = JsAccountServer;
            ReduxPlatform.AccountToken = JsAccountToken;
            ReduxPlatform.AccountName = JsAccountName;
            ReduxPlatform.OnlineBlockedReason = JsOnlineBlockedReason;
        }
    }
}
