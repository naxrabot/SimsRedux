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
        public const string Number = "1.0";
        public const string Stage = "Beta";

        /// <summary>Short lines for the update popup (what changed in this version).</summary>
        public static readonly string[] WhatsNew =
        {
            "The game's own families are now background families: click their house to Take Over (they join your families) or Just Watch them live their lives.",
            "Only your own families get the green plumbob.",
            "Starting the neighborhood fresh has moved to Options > Play > Neighborhood.",
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
