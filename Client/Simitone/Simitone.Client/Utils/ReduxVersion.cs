using FSO.Common.Utils;
using Simitone.Client.UI.Panels;
using System;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// SimsRedux: the game's version, shown on the neighbourhood top bar ("Redux / Beta / v0.1")
    /// and in the "The game has been updated" popup.
    ///
    /// EVERY UPDATE: raise ReduxBuild.Number by 0.1 and put a few lines about it in WhatsNew.
    /// </summary>
    public static class ReduxVersion
    {
        public const string Number = ReduxBuild.Number; // (the number itself is in ReduxBuild.cs)
        public const string Stage = ReduxBuild.Stage;

        /// <summary>Short lines for the update popup (what changed in this version).</summary>
        public static readonly string[] WhatsNew =
        {
            "Talking to Sims: still being worked on.",
            "Talking to Sims: townies and guests on community lots can be talked to again.",
            "No more trolleys: the Old Town trolley stops pulling up and piling up in front of your lot.",
            "Talking to Sims: more work on menus for guests, townies and shop staff.",
            "Talking to Sims: you can talk to guests, townies and shop staff again - your own Sims always count as family.",
            "Fewer popups: the cab company, Magic Town and Studio Town letters no longer pop up.",
            "Fixes: an error popup about a hidden lot controller is gone.",
            "Build mode groups: floors, wallpaper, walls, doors and plants have tabs - Carpet, Wood, Tile, Fences, Arches, Trees and more.",
            "Buy mode groups: big categories split into smaller tabs, like Art, Mirrors and Rugs in Decorative.",
            "Townies: lots have people around - some are already there when you arrive, and neighbours walk by your home.",
            "Call a taxi: use the phone to visit a neighbour, a community lot or Downtown.",
            "Smarter Sims: Sims find things to do on their own, and your orders always come first.",
            "Phone: calling a friend works again, even when your Sim doesn't know anyone yet.",
            "Coming soon: new floors, walls and wall pictures - they'll be tried out on the Test server first.",
            "Smarter Sims: a Sim stops retrying a chore they can't get to and comes back to it later.",
            "Launcher: you can still play single player while a new version is on its way.",
            "Fixes: lots of bug fixes and smoother play across the game, the launcher and online.",
            "Magic Town: families can move into the Magic Town lots again.",
            "Launcher: a new background, and a Refresh button when a new version is out.",
            "New logo: The Sims Redux logo on the neighborhood bar and the launcher.",
            "Family faces: each Sim's head is centred in its picture.",
            "Buy and Build: the icons sit right on the new bar.",
            "Neighborhood bar: tidier logo and version text, and a new Online button.",
            "Control panel: money is centred, and the clock and money text is smaller and tidier.",
            "Neighborhood bar: the top bar gets the new look, with the buttons sitting smoothly in it.",
            "New look: smoother control panel in the classic Sims blue, with the original icons.",
            "Interface options: pick the Redux or Classic look, a UI colour and a UI size (Options > Graphics > Interface).",
            "Server warnings: you're told before the server restarts or shuts down.",
            "Maintenance: the server can close for upkeep - you'll see it on the launcher.",
            "Moderator tools: moderators can help run the town from the launcher.",
            "Account sign-up: new accounts add your name and an email for password resets.",
            "Levels and XP: earn XP playing online and level up your account.",
            "Town announcements: server messages and events pop up in the online town.",
            "Launcher: pick the Live or Test server, see the message of the day, and report a player.",
            "Smarter Sims: they leave other Sims' plates alone and don't chat in doorways.",
            "New launcher: patch notes, your account and server status in one place.",
            "Fire safety: a grown-up calls the fire department when there's a fire.",
            "Repairman: Sims call the repairman when nobody at home can fix something.",
            "Quiet hours: Sims keep it down around sleeping Sims.",
            "Smarter Sims: they finish meals, sleep through the night, pay bills and fix things.",
            "Sim Smarts: a new Off setting turns off free will in single player.",
            "Smarter Sims: they take care of needs before they turn red and get ready for work.",
            "New mods: Red Poppy bathroom set, LPX soccer ball, Frosti floors, Zeta heads and more.",
            "Loading screens: going home or visiting shows the wallpaper until the lot is ready.",
            "Debug menu: Ctrl + Left Shift + right-click a stuck object to reset, move or delete it.",
            "Online: the game always runs at normal speed for everyone.",
            "Online: questions for your Sim, like picking a job, wait for your answer.",
            "Online: your Sim stays yours - nothing else can take control of them.",
            "Fewer pop-ups: the Help System and expansion announcements no longer appear.",
            "Online town: one clock for the whole town.",
            "Online town: visit a family while their player is home.",
            "Online: if your game crashes, your Sim leaves the lot too.",
            "Website: the game fills your browser window properly.",
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
