using FSO.SimAntics.Model;
using FSO.SimAntics.Primitives;
using System;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// SimsRedux: the game's own nag popups that are never shown - they're answered straight away
    /// (by whichever game runs the lot: single player, or the server for online lots), so the game
    /// carries on as if the player had answered.
    ///   "Help System" (turn the help system on?)       -> No
    ///   expansion announcements ("Old Town Shuttle"...) -> OK
    ///   expansion town welcome letters (the cab company, Magic Town, Studio Town) -> OK
    /// </summary>
    public static class ReduxDialogs
    {
        private static readonly string[] TownTitles = { "Downtown", "Magic Town", "Studio Town", "Vacation Island", "Old Town" };
        private static readonly string[] TownWords = { "cab company", "Studio Town Insider", "grand opening of Studio Town", "strange happenings at the edge of town" };

        /// <summary>The button to answer with (0 = first button), or null to show the popup as usual.</summary>
        public static byte? AutoAnswer(VMDialogInfo info)
        {
            if (info == null || !info.Block) return null;
            var title = (info.Title ?? "").Trim();
            var msg = (info.Message ?? "").Trim();
            if (title.Equals("Help System", StringComparison.OrdinalIgnoreCase)
                || msg.IndexOf("help system activated", StringComparison.OrdinalIgnoreCase) >= 0)
                return 1; // No
            if (title.EndsWith("Shuttle", StringComparison.OrdinalIgnoreCase)
                || (msg.StartsWith("You can now", StringComparison.OrdinalIgnoreCase) && msg.IndexOf("call", StringComparison.OrdinalIgnoreCase) >= 0
                    && (info.Operand == null || info.Operand.Type == VMDialogType.Message)))
                return 0; // OK
            if (IsTownLetter(info)) return 0; // OK
            return null;
        }

        /// <summary>The expansion towns' welcome letters (the new cab company, Magic Town, Studio Town...): never shown.</summary>
        public static bool IsTownLetter(VMDialogInfo info)
        {
            if (info == null) return false;
            if (!(info.Operand == null || info.Operand.Type == VMDialogType.Message)) return false;
            var title = (info.Title ?? "").Trim();
            var msg = (info.Message ?? "").Trim();
            foreach (var town in TownTitles)
                if (title.Equals(town, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var words in TownWords)
                if (msg.IndexOf(words, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
    }
}
