using Simitone.Client.UI.Panels;
using System;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// SimsRedux online: messages from the server (an admin's /say, events starting and ending, your
    /// own level-ups) pop up on screen. Checked every 5 seconds while you're in the online town -
    /// on a lot or on the town map. Never in single player.
    /// </summary>
    public static class ReduxNotices
    {
        private static DateTime Next;
        private static int Last = -1;
        private static bool Busy;

        public static void Tick()
        {
            if (ReduxWorker.Active || !ReduxTown.Active || !ReduxTown.LoggedIn || Busy) return;
            var now = DateTime.UtcNow;
            if (now < Next) return;
            Next = now + TimeSpan.FromSeconds(5);
            Busy = true;
            ReduxTown.FetchNotices(Last, (last, list) =>
            {
                Busy = false;
                bool first = Last < 0;
                Last = last;
                if (first || list.Count == 0) return; // (the first check only finds where the list is up to)
                foreach (var n in list)
                {
                    try { UIClassicDialog.Show(n.Item1, n.Item2, Tuple.Create<string, Action>("OK", () => { })); }
                    catch (Exception e) { Console.WriteLine("[SimsRedux] Couldn't show a server message: " + e.Message); }
                }
            });
        }
    }
}
