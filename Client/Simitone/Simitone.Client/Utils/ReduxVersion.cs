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
            "Computer: writing, novels, chatting, hacking and Twisted Metal 2 keep going now (they stopped after a few seconds), and skills and fun go up.",
            "New on the computer: Play Twisted Metal 2 - the game plays on the monitor while your Sim has fun.",
            "Online: the Writing skill and novels now go with your Sims to the online town and back.",
            "Computers do more, like The Sims 3: Practice Writing, Write a Novel (Romance, Sci-Fi, Mystery, Fantasy or Children's - finished novels sell), Surf the Web, Chat Online and Hack (Logic 7+).",
            "New skill: Writing - see it on the Job page next to the other skills.",
            "The pie menu has the new look.",
            "Touch: tap something already in your house (in Buy or Build mode) to pick it up, then drag it and tap to put it down - or use Turn / Place / Sell.",
            "Touch: the control panel is smaller, so Buy and Build show several columns of pictures. Options > Graphics > Interface > UI Size now sizes it too (with a new 75% size).",
            "321 new things to buy: BNJ x LVR chairs, bookcases, dressers, rugs and windows, Bara's 1940s diner set, and SML's IKEA Stuff Pack.",
            "Phones and tablets: the screen stays on while the game is open.",
            "Getting the game files from the server is much faster - many files per download, squeezed smaller - and only what's missing or changed is downloaded again.",
            "Touch: tapping a catalog picture picks the item up again, so you can buy and place things on a phone.",
            "Create-a-Sim: no more plain grey outfits, and the buttons show the original Sims 1 pictures again.",
            "Popups (Delete Family?, Yes / No, the phone and other messages) use the new look.",
            "Touch (upright phones): the clock and money box now reaches the bottom of the panel, with bigger numbers.",
            "Touch: the control panel runs a little smoother.",
            "Touch: a new, bigger control panel for phones and tablets - the Sims 1 panel grown for fingers. Live, Buy, Build and Options in a diamond, speed, camera and walls on the right, your family and the Mood / Job / ... tabs just above. Works held sideways or upright.",
            "Touch: the Turn Left and Turn Right arrows show just the arrows - no black square behind them.",
            "Tint: only the object's own main colour changes - a bookshelf's wood, not the books on it.",
            "Cross saves: your neighborhood, houses, Sims and settings are kept on your account - log in on any device and your game is there.",
            "Touch: the Redux interface (the Sims 1 control panel) now works on phones and tablets, sized for your fingers. Drag a held object to move it, tap to put it down, and use Turn / Place / Sell.",
            "Touch: long-press an object in Buy or Build mode to Tint it. Drag with one finger to move around the neighborhood.",
            "Bug fixes: Build mode's dropper works, the Tint window's colour dropper works in Build mode, Tint saves on things Sims are using, and typing in a box no longer changes game speed.",
            "Phones: the phone's keyboard opens for names and chat, and PC Controls on a tablet works with taps.",
            "Touch: taps land exactly where your finger is, and you can press, hold and drag to move the camera.",
            "Launcher: your account options are in a menu - click your initial next to your name.",
            "Create-A-Family and Create-A-Sim have the new Redux look: rounded panels and buttons in your UI colour.",
            "Touch camera: drag with one finger to move around, hold two fingers to pan, pinch to zoom and twist to turn.",
            "Phones: menus fit the screen (nothing cut off), whichever way up you hold it - turn the phone and the game follows.",
            "Touch controls now turn on for all Android phones and tablets, and a notice in the corner says which controls are on.",
            "Touch controls: on a phone or tablet the game switches to touch controls by itself. Change it in Options > Interface > Controls (Auto, Touch or PC).",
            "Phones: the Play button stays at the bottom of the screen while you scroll the launcher.",
            "Tint: Pick colour from lot copies another object's colour, and the hex code box shows (or takes) an exact colour.",
            "Tint: right-click an object in Buy or Build mode to recolour it - pick a colour (white and black too), then set Colour, Strength and Brightness.",
            "Bigger catalog: click the arrow on top of the Buy or Build pictures to show up to 5 rows at once.",
            "Cleaner icons: the small category icons in Buy mode are just the icons now, with a glow on the one you picked.",
            "Item cards: point at anything in Buy or Build mode to see a big picture, the price, the description and what it does.",
            "Notifications: notices pop up in the top-right corner and fade away (or close them with X) - they no longer pause the game.",
            "Buy and Build: the search box and dropper sit on the right of the bar, item details show on the classic bar, and tab names fit their tabs.",
            "Buy and Build: hidden test items with no picture no longer show up.",
            "Dropper: in Buy or Build mode, press I (or the Dropper button) and click anything on your lot - or a floor - to pick the same item from the catalog.",
            "Catalog search: type in the search box above the Buy and Build bar to find any item by name.",
            "New neighbours: new background families move into empty homes and new townies are out and about - on community lots and walking past your home.",
            "Online town: every home is free for players to move into - the town's families are out and about as townies instead.",
            "Online town: more neighbours and townies too - townies now show up on online lots, the same for everyone there.",
            "Shop staff: you can talk to clerks, waiters, chefs and other workers on community lots.",
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
