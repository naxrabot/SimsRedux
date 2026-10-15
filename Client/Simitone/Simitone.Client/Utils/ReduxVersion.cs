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
        public const string Number = "1.7";
        public const string Stage = "Beta";

        /// <summary>Short lines for the update popup (what changed in this version).</summary>
        public static readonly string[] WhatsNew =
        {
            "Online: coming home after visiting a friend brings your Sim back (in at the mailbox if they were away), and the camera never shows an empty grey screen.",
            "Online: buying something takes the money off straight away.",
            "Online: questions at home (like the carpool's \"time for work\") take your answer instead of popping up again and again.",
            "Website: no more crash from a sound when the game carries on after a question.",
            "Admin: give a player's town family money from the server panel ( /money <account> <amount> ).",
            "Server: the grey window that popped up when a lot opened (Go Home, Visit) is now invisible for good.",
            "Walls: diagonal walls stay visible when walls are cut away, and doors in cut walls hide with them.",
            "Online: Leave Multiplayer (on lots in the town and on the town bar) takes you straight back to your own neighborhood.",
            "Online: click your house and choose Move Out to delete your online family, then make a new one or bring another.",
            "Online: your family's money shows at home, and the town's plumbob sits just above your roof.",
            "Server: opening a lot no longer flashes a grey game window on the server PC.",
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
