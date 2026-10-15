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
        public const string Number = "3.0";
        public const string Stage = "Beta";

        /// <summary>Short lines for the update popup (what changed in this version).</summary>
        public static readonly string[] WhatsNew =
        {
            "Smarter Sims, stage 2: they finish what they start (the whole meal, a proper sleep), stop bouncing between the same things, pay the bills and repair broken things when they're doing fine, and grab a snack instead of cooking when they can't cook.",
            "Options > Play > Sim Smarts now has Off (your family does nothing on their own in single player).",
            "Smarter Sims: they deal with needs before they turn red, put urgent needs first, sleep at night instead of napping all day, and get ready before the carpool. Options > Play > Sim Smarts (online is always Smartest).",
            "Mods added: Red Poppy bathroom set and French door, LPX soccer ball, Frosti chevron floors, 7 Zeta heads, Hidden Objects Unlocked, Improved Pets, and Slower Motives.",
            "Online: going home or visiting shows the wallpaper and progress bar until the lot is actually on screen - no more grey screen.",
            "Debug menu works on objects: Ctrl + Left Shift + right-click a stuck object (in any mode) to Reset it, Move it to a free spot, or Delete it for its value back.",
            "Website: the Sims Redux wallpaper is now behind the first screen (title, log in, game files).",
            "Online: the game never pauses - always normal speed for everyone, even in Buy and Build mode. A question only holds up the Sim it's about while everyone else carries on.",
            "The update message is short now - press Show More to read everything that changed, a page at a time.",
            "Online: questions for your Sim (like picking a job) wait for your answer - the server no longer answers for you after 20 seconds.",
            "Online: nothing on the server can take your Sim away from you while you're controlling them.",
            "Online: your Sim no longer flickers on and off - the server's own copy of the lot kept taking control of them.",
            "The Help System question and the expansion announcements (like Old Town Shuttle) don't pop up any more.",
            "Online: no more crash when someone arrives on a lot while it's still loading.",
            "Online: messages like \"nobody's home\" stay on screen until you press OK.",
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

            // short: one line about the update, with the full list a page at a time behind Show More
            var msg = "The Sims Redux has been updated to " + Stage + " " + Text + ".";
            if (WhatsNew.Length > 0) msg += "\n\n" + WhatsNew[0] + (WhatsNew.Length > 1 ? "\n\n(and " + (WhatsNew.Length - 1) + " more)" : "");
            GameThread.NextUpdate((s) =>
            {
                try
                {
                    var buttons = new System.Collections.Generic.List<Tuple<string, Action>> { Tuple.Create<string, Action>("OK", MarkSeen) };
                    if (WhatsNew.Length > 1) buttons.Add(Tuple.Create<string, Action>("Show More", () => ShowPage(0)));
                    UIClassicDialog.Show("Updated to " + Text, msg, buttons.ToArray());
                }
                catch (Exception e) { Console.WriteLine("[SimsRedux] Couldn't show the update popup: " + e.Message); }
            });
        }

        /// <summary>How many changes fit on one page of Show More (so it never runs off the screen).</summary>
        private const int PerPage = 4;

        /// <summary>One page of everything that changed, with Back / Next / Close.</summary>
        private static void ShowPage(int page)
        {
            int pages = (WhatsNew.Length + PerPage - 1) / PerPage;
            page = Math.Max(0, Math.Min(pages - 1, page));
            var lines = new System.Collections.Generic.List<string>();
            for (int i = page * PerPage; i < Math.Min(WhatsNew.Length, (page + 1) * PerPage); i++) lines.Add("- " + WhatsNew[i]);
            var buttons = new System.Collections.Generic.List<Tuple<string, Action>>();
            if (page > 0) buttons.Add(Tuple.Create<string, Action>("Back", () => ShowPage(page - 1)));
            if (page < pages - 1) buttons.Add(Tuple.Create<string, Action>("Next", () => ShowPage(page + 1)));
            buttons.Add(Tuple.Create<string, Action>("Close", MarkSeen));
            try { UIClassicDialog.Show("What's New in " + Text + " (" + (page + 1) + " of " + pages + ")", string.Join("\n\n", lines), buttons.ToArray()); }
            catch (Exception e) { Console.WriteLine("[SimsRedux] Couldn't show what's new: " + e.Message); }
        }

        /// <summary>The popup only counts as seen once it's closed (if it got covered up, it shows again next time).</summary>
        private static void MarkSeen()
        {
            try { ReduxSettings.Set("seen_version", Number); } catch { }
        }
    }
}
