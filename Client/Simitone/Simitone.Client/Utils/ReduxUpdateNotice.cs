using FSO.Client.UI.Framework;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Screens;
using System;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// SimsRedux: "New update available". When a newer version of the game is out (the website has
    /// it, and the server players use has been updated to it too), a notice with an Update button
    /// pops up in the corner. Update asks whether to save first, then reloads the game with the new
    /// version. Closing the notice puts it off for a while; it comes back later.
    /// (The browser version only: the Windows game is updated by rebuilding it.)
    /// </summary>
    public static class ReduxUpdateNotice
    {
        private static DateTime NextCheck = DateTime.MinValue;
        private static string ShownFor;
        private static DateTime ShownAt;
        private static readonly TimeSpan Again = TimeSpan.FromMinutes(20);

        /// <summary>Every frame, from the game screen (it only looks every few seconds).</summary>
        public static void Tick(TS1GameScreen screen)
        {
            if (ReduxPlatform.UpdateAvailable == null || ReduxPlatform.ReloadForUpdate == null) return;
            var now = DateTime.UtcNow;
            if (now < NextCheck) return;
            NextCheck = now.AddSeconds(10);
            string version;
            try { version = ReduxPlatform.UpdateAvailable(); } catch { return; }
            if (string.IsNullOrEmpty(version)) return;
            if (version == ShownFor && now - ShownAt < Again) return;
            ShownFor = version;
            ShownAt = now;
            UIReduxToasts.ShowAction("New update available",
                "Version " + version + " of The Sims Redux is ready (you have " + ReduxVersion.Number + ").",
                "Update", () => AskAndUpdate(screen));
        }

        private static void AskAndUpdate(TS1GameScreen screen)
        {
            var current = UIScreen.Current as TS1GameScreen ?? screen;
            bool canSave = current != null && current.vm != null && !current.IsGuest && !current.Watching;
            if (canSave)
            {
                UIClassicDialog.Show("Update the Game",
                    "The game will close and open again with the new version.\n\nDo you want to save your game first?",
                    Tuple.Create("Save and Update", (Action)(() =>
                    {
                        try { current.Save(); }
                        catch (Exception e) { Console.WriteLine("[SimsRedux] save before updating failed: " + e.Message); }
                        Reload();
                    })),
                    Tuple.Create("Update Without Saving", (Action)Reload),
                    Tuple.Create("Not Now", (Action)null));
            }
            else
            {
                UIClassicDialog.Show("Update the Game", "The game will close and open again with the new version.",
                    Tuple.Create("Update", (Action)Reload),
                    Tuple.Create("Not Now", (Action)null));
            }
        }

        private static void Reload()
        {
            UIReduxToasts.Show("Updating", "Getting the new version - the game will reload in a moment...");
            try { ReduxPlatform.ReloadForUpdate(); }
            catch (Exception e) { Console.WriteLine("[SimsRedux] reload for update failed: " + e.Message); }
        }
    }
}
