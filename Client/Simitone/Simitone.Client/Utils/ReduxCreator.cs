using FSO.Common;
using System;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// SimsRedux: content made with the Sims Redux Creator (Content/redux/creator/) is tried out on the
    /// Test server first. It's loaded when:
    ///   - the game is playing on the Test server (its address has "tsr-test" in it, or port 37566),
    ///   - it's a lot worker of the Test server (started with -creator), or
    ///   - the "creatorcontent=1" setting is on (to look at new items on your own PC before sending them out).
    /// Live players don't get it until it's promoted (moved into the shipped mods).
    /// </summary>
    public static class ReduxCreator
    {
        public static bool ShouldLoad()
        {
            try
            {
                var args = FSOEnvironment.Args ?? "";
                if (args.Contains("-creator")) return true;
                if (ReduxSettings.GetBool("creatorcontent", false)) return true;
                var server = ReduxTown.Server ?? "";
                return server.IndexOf("tsr-test", StringComparison.OrdinalIgnoreCase) >= 0 || server.EndsWith(":37566");
            }
            catch (Exception e)
            {
                Console.WriteLine("[SimsRedux] Creator content check: " + e.Message);
                return false;
            }
        }
    }
}
