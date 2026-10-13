using FSO.Client;
using FSO.SimAntics;
using FSO.LotView.Components;
using Microsoft.JSInterop;
using Simitone.Client.UI.Screens;
using System.Text;

namespace Simitone.Web
{
    /// <summary>
    /// SimsRedux web: a live status report of the lot (open the site with ?dbg=vm), so problems
    /// like invisible Sims can be seen from the browser without a debugger.
    /// </summary>
    public static class WebDebug
    {
        [JSInvokable]
        public static string VMStatus()
        {
            try
            {
                var screen = GameFacade.Screens?.CurrentUIScreen as TS1GameScreen;
                if (screen == null) return "Not on a lot (screen: " + (GameFacade.Screens?.CurrentUIScreen?.GetType().Name ?? "none") + ")";
                var vm = screen.vm;
                if (vm == null) return "No lot loaded yet (neighborhood)";
                var sb = new StringBuilder();
                sb.AppendLine("tick " + vm.Scheduler.CurrentTickID + "  myUID " + vm.MyUID + "  driver " + (screen.Driver?.GetType().Name ?? "none") + "  speed " + vm.SpeedMultiplier);
                sb.AppendLine("objects " + vm.Entities.Count + "  avatars " + vm.Context.ObjectQueries.Avatars.Count);
                var world = vm.Context.World;
                if (world != null) sb.AppendLine("camera centre " + world.State.CenterTile + "  zoom " + world.State.Zoom + "  level " + world.State.Level);
                foreach (var ent in vm.Context.ObjectQueries.Avatars)
                {
                    var ava = ent as VMAvatar;
                    if (ava == null) continue;
                    var comp = ava.WorldUI as AvatarComponent;
                    var model = comp?.Avatar;
                    sb.Append("- " + ava.Name + " pid " + ava.PersistID + " obj " + ava.ObjectID);
                    sb.Append(" pos " + ava.Position + " visual " + ava.VisualPosition);
                    sb.Append(ava.Dead ? " DEAD" : "");
                    if (comp == null) sb.Append(" (no world component)");
                    else
                    {
                        sb.Append(" visible " + comp.Visible);
                        if (model == null) sb.Append(" (no body model)");
                        else sb.Append(" body parts " + model.Bindings.Count + (model.Skeleton == null ? " NO SKELETON" : "") + " anims " + (ava.Animations?.Count ?? 0));
                    }
                    var thread = ava.Thread;
                    if (thread != null) sb.Append(" actions " + thread.Queue.Count + " stack " + thread.Stack.Count);
                    sb.AppendLine();
                }
                lock (FSO.SimAntics.NetPlay.VMNetDriver.DebugLog)
                {
                    sb.AppendLine("last commands:");
                    foreach (var line in FSO.SimAntics.NetPlay.VMNetDriver.DebugLog) sb.AppendLine("  " + line);
                }
                return sb.ToString();
            }
            catch (System.Exception e)
            {
                return "status error: " + e.Message;
            }
        }
    }
}
