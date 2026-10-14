using FSO.Client;
using FSO.Common;
using FSO.Content;
using FSO.SimAntics.NetPlay.Model.Commands;
using Simitone.Client.UI.Screens;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// SimsRedux dedicated server: the game started by the server as a "lot worker".
    ///
    ///   Simitone.exe -path"&lt;Sims folder&gt;" -worker -house12 -port37600 -town"&lt;town folder&gt;"
    ///
    /// It opens one house from the server's town (not the player's own neighbourhood), hosts it
    /// online on the given port with no window showing, and the server passes players to it.
    /// When everyone has left it saves the lot into the town and closes. It talks to the server
    /// through its console: lines starting "[worker]" out, and "save" / "quit" in.
    /// </summary>
    public static class ReduxWorker
    {
        public static bool Active;
        public static short House;
        public static int Port;
        /// <summary>The town's UserData folder (Neighborhood.iff, Houses, Characters...).</summary>
        public static string Town;

        /// <summary>How long an empty lot stays open (for someone coming back) before it saves and closes.</summary>
        private static readonly TimeSpan EmptyGrace = TimeSpan.FromMinutes(2);
        /// <summary>How long a lot waits for its first player before giving up.</summary>
        private static readonly TimeSpan FirstPlayerWait = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan AutoSave = TimeSpan.FromMinutes(10);

        private static TS1GameScreen Screen;
        private static DateTime Started, EmptySince, LastSave, BlockedSince;
        private static bool EverHadPlayers, Closing;
        private static int LastCount = -1;
        private static string Command; // from the server, read on its own thread

        /// <summary>Reads the worker arguments (from Program.cs). Returns true if this is a worker.</summary>
        public static bool Configure(string[] args)
        {
            foreach (var a in args)
            {
                if (!a.StartsWith("-")) continue;
                var s = a.Substring(1);
                if (s == "worker") Active = true;
                else if (s.StartsWith("house") && short.TryParse(s.Substring(5), out var h)) House = h;
                else if (s.StartsWith("port") && int.TryParse(s.Substring(4), out var p)) Port = p;
                else if (s.StartsWith("town")) Town = s.Substring(4).Trim('"').Replace('\\', '/').TrimEnd('/') + "/";
            }
            if (!Active) return false;
            if (House <= 0 || Port <= 0 || string.IsNullOrEmpty(Town))
            {
                Say("error bad arguments (need -house<N> -port<N> -town<folder>)");
                Environment.Exit(2);
            }
            // the game's normal settings folder is used as-is (it starts up exactly like the game);
            // the lot itself is only ever loaded from and saved into the town folder
            // sound stays switched on (the game expects it everywhere) but at volume 0 - see SimitoneGame
            Started = DateTime.UtcNow;

            var input = new Thread(() =>
            {
                try
                {
                    string line;
                    while ((line = Console.ReadLine()) != null) Interlocked.Exchange(ref Command, line.Trim().ToLowerInvariant());
                }
                catch { }
            }) { IsBackground = true, Name = "worker console" };
            input.Start();
            Say("starting house " + House + " on port " + Port);
            return true;
        }

        public static void Say(string text)
        {
            // one line per message: the server reads them line by line
            text = (text ?? "").Replace("\r", "").Replace("\n", " | ");
            try { Console.WriteLine("[worker] " + text); Console.Out.Flush(); } catch { }
        }

        /// <summary>A crash: the whole report to the server, and to a file next to the town.</summary>
        public static void Crashed(object exception)
        {
            Say("error crashed: " + exception);
            try
            {
                var file = Path.Combine(Path.GetDirectoryName(Town.TrimEnd('/')) ?? ".", "worker-crash-house" + House + ".txt");
                File.WriteAllText(file, DateTime.Now + "\r\n" + exception);
            }
            catch { }
        }

        /// <summary>Called once the game screen exists: load the town and open the house.</summary>
        public static void Begin(TS1GameScreen screen)
        {
            Screen = screen;
            try
            {
                var hood = Content.Get().Neighborhood;
                hood.LoadFolder(Town);
                if (!File.Exists(hood.GetHousePath(House)))
                {
                    Say("error there's no house " + House + " in the town");
                    Environment.Exit(3);
                }
                HideWindow();
                screen.HostAfterLoad = true;
                screen.PlayHouse(House, null);
                Say("loading house " + House);
            }
            catch (Exception e)
            {
                Say("error couldn't open the house: " + e.Message);
                Environment.Exit(4);
            }
        }

        /// <summary>Every frame: report players, answer stuck questions, save, and close when empty.</summary>
        public static void Tick()
        {
            if (!Active || Screen == null || Closing) return;
            var now = DateTime.UtcNow;
            var host = Screen.Host;

            var cmd = Interlocked.Exchange(ref Command, null);
            if (cmd == "save") { SaveNow(); }
            else if (cmd == "quit") { Quit("asked to close"); return; }

            if (host == null)
            {
                if (now - Started > TimeSpan.FromMinutes(3)) Quit("the lot never finished loading");
                return;
            }
            if (LastCount < 0)
            {
                Say("hosting port " + Port);
                LastSave = now;
                EmptySince = now;
                LastCount = 0;
            }

            int count = host.GuestCount;
            if (count != LastCount)
            {
                Say("players " + count + (count > 0 ? " " + string.Join(", ", host.GuestNames) : ""));
                if (count > 0) EverHadPlayers = true;
                if (count == 0) EmptySince = now;
                LastCount = count;
            }

            // nobody is at the keyboard on a worker: a question that pauses the game gets the
            // default answer after a while so the lot carries on
            var vm = Screen.vm;
            if (vm?.GlobalBlockingDialog != null)
            {
                if (BlockedSince == default) BlockedSince = now;
                else if (now - BlockedSince > TimeSpan.FromSeconds(20))
                {
                    vm.SendCommand(new VMNetDialogResponseCmd { ResponseCode = 0, ResponseText = "" });
                    BlockedSince = default;
                }
            }
            else BlockedSince = default;

            if (now - LastSave > AutoSave) SaveNow();

            if (host.ConnectionCount > 0) EmptySince = now; // someone is still joining
            if (count == 0 && (EverHadPlayers ? now - EmptySince > EmptyGrace : now - Started > FirstPlayerWait))
                Quit(EverHadPlayers ? "everyone left" : "nobody came");
        }

        private static void SaveNow()
        {
            try
            {
                Screen.Save();
                LastSave = DateTime.UtcNow;
                Say("saved");
            }
            catch (Exception e) { Say("error saving: " + e.Message); }
        }

        private static void Quit(string why)
        {
            Closing = true;
            SaveNow();
            Say("closing (" + why + ")");
            try { Screen.Host?.Stop("The lot is closing."); } catch { }
            Environment.Exit(0);
        }

        // ---------- no window on the server's screen ----------

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public static void HideWindow()
        {
            if (!OperatingSystem.IsWindows()) return;
            try
            {
                var handle = GameFacade.Game?.Window?.Handle ?? IntPtr.Zero;
                if (handle != IntPtr.Zero) ShowWindow(handle, 0); // SW_HIDE
            }
            catch { }
        }
    }
}
