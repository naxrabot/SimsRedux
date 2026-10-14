using FSO.Common;
using FSO.Common.IO;
using FSO.Content;
using FSO.Content.TS1;
using FSO.Client.UI.Framework;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Screens;
using System;
using System.IO;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// SimsRedux: starting the single-player neighbourhood over with the game's starting families
    /// (the Goths, Newbies, Pleasants and the rest). The current neighbourhood is kept as a backup.
    ///
    /// Offered once, on the neighbourhood screen, to anyone whose neighbourhood has no families
    /// at all (an earlier version started new games from the game's blank template). It can be
    /// asked for again any time with the cheat "freshneighborhood".
    /// </summary>
    public static class ReduxFreshHood
    {
        private static bool AskedThisRun;

        private static string LocalFolder { get { return Path.Combine(FSOEnvironment.UserDir, "UserData/"); } }

        private static bool PendingAsk;

        /// <summary>The house numbers that have a family living in them, in a neighbourhood folder.</summary>
        private static System.Collections.Generic.HashSet<int> HousedHouses(string folder)
        {
            var set = new System.Collections.Generic.HashSet<int>();
            try
            {
                var nbhd = Path.Combine(folder, "Neighborhood.iff");
                if (!FSOFile.Exists(nbhd)) return set;
                var iff = new FSO.Files.Formats.IFF.IffFile(nbhd);
                foreach (var f in iff.List<FSO.Files.Formats.IFF.Chunks.FAMI>() ?? new System.Collections.Generic.List<FSO.Files.Formats.IFF.Chunks.FAMI>())
                    if (f.HouseNumber > 0) set.Add((int)f.HouseNumber);
            }
            catch { }
            return set;
        }

        /// <summary>
        /// On the neighbourhood screen: offer a fresh start (once) if none of the game's starting
        /// families live here any more - or if it was asked for with the cheat while on a lot.
        /// </summary>
        public static void OfferIfEmpty()
        {
            if (ReduxWorker.Active || ReduxTown.Active) return;
            if (PendingAsk)
            {
                PendingAsk = false;
                FSO.Common.Utils.GameThread.NextUpdate((s) => Ask(false));
                return;
            }
            if (AskedThisRun) return;
            AskedThisRun = true;
            try
            {
                if (ReduxSettings.GetBool("freshhoodoffered", false)) return;
                var originals = HousedHouses(Content.Get().Neighborhood.FreshSource());
                if (originals.Count == 0) return; // nothing better to offer
                var mine = HousedHouses(LocalFolder);
                if (mine.Overlaps(originals)) return; // the starting families are still here
            }
            catch (Exception e)
            {
                Console.WriteLine("[SimsRedux] fresh neighbourhood check: " + e.Message);
                return;
            }
            FSO.Common.Utils.GameThread.NextUpdate((s) => Ask(true));
        }

        /// <summary>Asks, then starts over on Yes. "first" = the automatic offer (remembered if declined).</summary>
        public static void Ask(bool first)
        {
            var screen = UIScreen.Current as TS1GameScreen;
            if (screen == null) return;
            if (screen.vm != null)
            {
                PendingAsk = true;
                UIClassicDialog.Ok("Fresh Neighborhood", "Go back to the neighborhood and you'll be asked whether to start it fresh.");
                return;
            }
            if (ReduxTown.Active)
            {
                UIClassicDialog.Ok("Fresh Neighborhood", "This only changes your own neighborhood. Leave the online town first.");
                return;
            }
            var message = (first ? "None of the game's starting families live in your neighborhood.\n\n" : "")
                + "Start a fresh neighborhood with the original families (the Goths, Newbies, Pleasants and the rest)?\n\n"
                + "Your current neighborhood, with any families and houses you made, is kept as a backup.";
            UIClassicDialog.Show("Fresh Neighborhood", message,
                Tuple.Create<string, Action>("Start Fresh", () => Reset(screen)),
                Tuple.Create<string, Action>("Not Now", () => { if (first) ReduxSettings.Set("freshhoodoffered", true); }));
        }

        private static void Reset(TS1GameScreen screen)
        {
            ReduxSettings.Set("freshhoodoffered", true);
            var local = LocalFolder;
            string backup = null;
            try
            {
                if (FSODirectory.Exists(local))
                {
                    backup = Path.Combine(FSOEnvironment.UserDir, "UserData-backup-" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "/");
                    CopyFolder(local, backup);
                    FSODirectory.Delete(local, true);
                }
                Content.Get().Neighborhood.InitSpecific(0); // copies the game's starting neighbourhood in
                Console.WriteLine("[SimsRedux] started a fresh neighbourhood" + (backup != null ? " (the old one is in " + backup + ")" : ""));
            }
            catch (Exception e)
            {
                Console.WriteLine("[SimsRedux] fresh neighbourhood failed: " + e);
                UIClassicDialog.Ok("Fresh Neighborhood", "Sorry, the neighborhood couldn't be started over: " + e.Message
                    + (backup != null ? "\n\nYour old neighborhood is in " + backup : ""));
            }
            screen.RefreshNeighbourhoodScreen();
        }

        private static void CopyFolder(string from, string to)
        {
            from = from.Replace('\\', '/');
            if (!from.EndsWith("/")) from += "/";
            FSODirectory.CreateDirectory(to);
            foreach (var f in FSODirectory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(from, f).Replace('\\', '/');
                if (rel.StartsWith("..")) continue;
                var target = Path.Combine(to, rel);
                FSODirectory.CreateDirectory(Path.GetDirectoryName(target));
                FSOFile.Copy(f, target, true);
            }
        }
    }
}
