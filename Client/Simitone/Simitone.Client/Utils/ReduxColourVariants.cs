using FSO.Content;
using FSO.Content.Interfaces;
using FSO.Files.Formats.IFF.Chunks;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// SimsRedux: finds the other colours/styles of an object, for colour swapping.
    ///
    /// Two catalog objects count as colours of each other when either:
    ///   - one is a recolour (clone) of the other: recolours point at their original
    ///     through the object's "original GUID", or
    ///   - they're in the same Buy Mode category and have exactly the same catalog name
    ///     (that's how the game itself names its colour versions).
    /// Objects that just share a file or behaviour are NOT grouped any more.
    /// Worked out one category at a time, the first time it's needed.
    /// </summary>
    public static class ReduxColourVariants
    {
        private static Dictionary<uint, List<uint>> Groups = new Dictionary<uint, List<uint>>();
        private static HashSet<int> BuiltCategories = new HashSet<int>();

        /// <summary>All colours of this object, cheapest first (includes the object itself).</summary>
        public static List<uint> Get(uint guid)
        {
            try
            {
                var catalog = Content.Get().WorldCatalog;
                var item = catalog.GetItemByGUID(guid);
                if (item != null && !BuiltCategories.Contains(item.Value.Category)) Build(item.Value.Category);
            }
            catch (Exception e)
            {
                Console.WriteLine("[SimsRedux] Couldn't build the colour list: " + e.Message);
            }
            List<uint> result;
            if (Groups.TryGetValue(guid, out result)) return result;
            return new List<uint> { guid };
        }

        private static Dictionary<uint, uint> Parent = new Dictionary<uint, uint>();
        private static uint Find(uint x)
        {
            uint p;
            if (!Parent.TryGetValue(x, out p)) { Parent[x] = x; return x; }
            if (p == x) return x;
            var root = Find(p);
            Parent[x] = root;
            return root;
        }
        private static void Union(uint a, uint b)
        {
            var ra = Find(a); var rb = Find(b);
            if (ra != rb) Parent[ra] = rb;
        }

        private static string CatalogName(GameObject obj, ObjectCatalogItem item)
        {
            string name = null;
            try { name = obj?.Resource?.Get<CTSS>(obj.OBJ.CatalogStringsID)?.GetString(0); } catch { }
            if (string.IsNullOrWhiteSpace(name)) name = item.Name;
            return (name ?? "").Trim().ToLowerInvariant();
        }

        private static void Build(sbyte category)
        {
            BuiltCategories.Add(category);
            var content = Content.Get();
            var catalog = content.WorldCatalog;
            var items = catalog.GetItemsByCategory(category);
            var prices = new Dictionary<uint, uint>();
            var byName = new Dictionary<string, uint>();

            foreach (var item in items)
            {
                var guid = item.GUID;
                if (prices.ContainsKey(guid)) continue;
                prices[guid] = item.Price;
                Find(guid);

                var obj = content.WorldObjects.Get(guid);
                if (obj == null) continue;

                // recolour of another object in this category
                var objd = obj.OBJ;
                uint original = ((uint)objd.OriginalGUID2 << 16) | objd.OriginalGUID1;
                if (original != 0 && original != guid)
                {
                    var orig = catalog.GetItemByGUID(original);
                    if (orig != null && orig.Value.Category == category) Union(guid, original);
                }

                // same name in the same category
                var name = CatalogName(obj, item);
                if (name.Length == 0) continue;
                uint first;
                if (byName.TryGetValue(name, out first)) Union(guid, first);
                else byName[name] = guid;
            }

            var lists = new Dictionary<uint, List<uint>>();
            foreach (var guid in prices.Keys)
            {
                var root = Find(guid);
                List<uint> list;
                if (!lists.TryGetValue(root, out list)) { list = new List<uint>(); lists[root] = list; }
                list.Add(guid);
            }
            foreach (var list in lists.Values)
            {
                list.Sort((a, b) => prices[a].CompareTo(prices[b]));
                foreach (var guid in list) Groups[guid] = list;
            }
        }
    }
}
