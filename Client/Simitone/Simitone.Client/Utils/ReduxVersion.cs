using FSO.Common.Utils;
using Simitone.Client.UI.Panels;
using System;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// SimsRedux: the game's version, shown on the neighbourhood top bar ("Redux / Beta / v0.1")
    /// and in the "The game has been updated" popup.
    ///
    /// EVERY UPDATE: raise Number by 0.1 and put a few lines about it in WhatsNew.
    /// </summary>
    public static class ReduxVersion
    {
        public const string Number = "2.1";
        public const string Stage = "Beta";

        /// <summary>Short lines for the update popup (what changed in this version).</summary>
        public static readonly string[] WhatsNew =
        {
            "Online: no more crash when a Sim arrives on a lot that's still loading.",
            "Online: messages like \"Nobody's home\" stay on screen until you press OK (they used to vanish straight away).",
            "Online: when the server can't let you onto a lot, it now says why (instead of \"couldn't reach the host\"), and your login code is never shown on screen.",
            "Online: games on different versions now say \"update your game\" instead of getting stuck on a grey screen.",
            "Online town: one clock for the whole town - every lot shows the same time, and it only moves while someone is playing (it can't be paused or sped up).",
            "Online town: a player's family only lives while their player is home - you can visit while they're there, and when they leave, visitors head back to the town.",
            "Online town: several lots can be open at once without undoing each other's saves (money, friendships, everything).",
            "Online: if your game crashes or freezes, you and your Sim are taken off the lot instead of your Sim carrying on alone.",
            "Online: going home while your Sim is at work no longer disconnects you - the camera shows your house until they're back.",
            "Website: the game fills the browser window properly from the start, and follows it when you resize.",
            "Website: no more crash from a sound when the game carries on after a question.",
        };

        public static string Text { get { return "v" + Number; } }

        private static bool Checked;

        /// <summary>Once per run: if this version hasn't been seen on this computer/browser yet, say so.</summary>
        public static void ShowUpdatePopupOnce()
        {
            if (Checked || ReduxWorker.Active) return;
            Checked = true;
            string seen;
            try { seen = ReduxSettings.GetString("seen_version", ""); }
            catch { seen = ""; }
            if (seen == Number) return;

            var msg = "The Sims Redux has been updated to " + Stage + " " + Text + ".";
            if (WhatsNew.Length > 0) msg += "\n\nWhat's new:\n- " + string.Join("\n- ", WhatsNew);
            GameThread.NextUpdate((s) =>
            {
                // only counts as seen once OK is pressed (if it got covered up, it shows again next time)
                try { UIClassicDialog.Ok("Updated to " + Text, msg, () => { try { ReduxSettings.Set("seen_version", Number); } catch { } }); }
                catch (Exception e) { Console.WriteLine("[SimsRedux] Couldn't show the update popup: " + e.Message); }
            });
        }
    }
}
