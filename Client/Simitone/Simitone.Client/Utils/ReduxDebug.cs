using FSO.Client;
using FSO.SimAntics;
using FSO.SimAntics.Model;
using FSO.SimAntics.NetPlay.Model.Commands;
using Simitone.Client.UI.Panels;
using System;
using System.Linq;
using System.Text;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// SimsRedux debug menu. Type the cheat "debugredux" (Ctrl + Shift + C), then hold
    /// Ctrl + Left Shift and right-click a Sim for fixes like un-sticking them.
    /// Lasts until the game is closed; "debugredux off" turns it off.
    /// </summary>
    public static class ReduxDebug
    {
        public static bool Enabled;

        /// <summary>Handles the cheat text. Returns true if it was a debugredux cheat.</summary>
        public static bool TryCheat(string text)
        {
            var t = (text ?? "").Trim().ToLowerInvariant();
            if (t == "freshneighborhood" || t == "freshneighbourhood") { ReduxFreshHood.Ask(false); return true; }
            if (t != "debugredux" && t != "debugredux on" && t != "debugredux off") return false;
            Enabled = t != "debugredux off";
            UIClassicDialog.Ok("Redux Debug", Enabled
                ? "Debug menu is ON.\n\nHold Ctrl + Left Shift and right-click a Sim to open it.\nType \"debugredux off\" to turn it off."
                : "Debug menu is OFF.");
            return true;
        }

        public static void ShowMenu(VM vm, VMAvatar sim)
        {
            if (vm == null || sim == null) return;
            UIClassicDialog.Show("Redux Debug: " + sim.Name, "What do you want to do with this Sim?",
                Tuple.Create<string, Action>("Reset Sim", () => vm.SendCommand(new VMNetResetSimCmd { ObjectID = sim.ObjectID })),
                Tuple.Create<string, Action>("Move to free spot", () => vm.SendCommand(new VMNetDebugSimCmd { ObjectID = sim.ObjectID, Action = VMNetDebugSimCmd.DebugAction.MoveToFreeSpot })),
                Tuple.Create<string, Action>("Fill needs", () => vm.SendCommand(new VMNetDebugSimCmd { ObjectID = sim.ObjectID, Action = VMNetDebugSimCmd.DebugAction.FillNeeds })),
                Tuple.Create<string, Action>("Sim info", () => UIClassicDialog.Ok("Sim info: " + sim.Name, Info(sim))),
                Tuple.Create<string, Action>("Cancel", (Action)null));
        }

        private static string Info(VMAvatar sim)
        {
            var sb = new StringBuilder();
            sb.Append("Position: tile " + sim.Position.TileX + ", " + sim.Position.TileY + "  floor " + sim.Position.Level + "\n");
            var q = sim.Thread?.Queue;
            sb.Append("Actions queued: " + (q?.Count ?? 0));
            if (q != null && q.Count > 0) sb.Append("  (doing: " + q[0].Name + ")");
            sb.Append("\nScript depth: " + (sim.Thread?.Stack.Count ?? 0));
            var top = sim.Thread?.Stack.LastOrDefault();
            if (top != null) sb.Append("  (in: " + top.Routine?.Rti?.Name + ")");
            sb.Append("\n\nHunger " + sim.GetMotiveData(VMMotive.Hunger) + "  Energy " + sim.GetMotiveData(VMMotive.Energy)
                + "  Comfort " + sim.GetMotiveData(VMMotive.Comfort) + "  Fun " + sim.GetMotiveData(VMMotive.Fun)
                + "\nHygiene " + sim.GetMotiveData(VMMotive.Hygiene) + "  Bladder " + sim.GetMotiveData(VMMotive.Bladder)
                + "  Social " + sim.GetMotiveData(VMMotive.Social) + "  Room " + sim.GetMotiveData(VMMotive.Room));
            return sb.ToString();
        }
    }
}
