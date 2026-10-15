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
    /// </summary>
    public static class ReduxDialogs
    {
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
            return null;
        }
    }
}
