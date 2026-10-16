using System;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// SimsRedux: things only some versions of the game can do. The Windows game leaves these
    /// empty; the browser version fills them in (Simitone.Web/WebStartup.cs), and the options
    /// screen shows the matching settings only when they're available.
    /// </summary>
    public static class ReduxPlatform
    {
        /// <summary>Can this version host a lot for friends? (Browsers can't accept connections.)</summary>
        public static bool CanHost = true;

        /// <summary>Forget the stored Sims files and ask for the Sims folder again (browser).</summary>
        public static Action ChangeGameFolder;

        /// <summary>Switch the browser's full screen on or off (browser).</summary>
        public static Action<bool> SetFullScreen;
        public static Func<bool> IsFullScreen;

        /// <summary>The account the website is logged in with (browser): server address, token, name. Empty when none.</summary>
        public static Func<string> AccountServer, AccountToken, AccountName;

        /// <summary>
        /// SimsRedux: the browser version started out of date (the website has a newer game coming):
        /// returns why online mode is shut, or "" / null when it's open.
        /// </summary>
        public static Func<string> OnlineBlockedReason;

        /// <summary>Why online mode can't be used right now, or null if it can.</summary>
        public static string OnlineBlocked()
        {
            try { var r = OnlineBlockedReason?.Invoke(); return string.IsNullOrEmpty(r) ? null : r; }
            catch { return null; }
        }
    }
}
