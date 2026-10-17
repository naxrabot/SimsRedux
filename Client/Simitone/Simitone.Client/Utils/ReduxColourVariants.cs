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
    ///   - one is the game's own recolour of the other (it points at its original through the
    ///     object's "original GUID") - only between the game's own objects: downloads often point
    ///     at the game object they were copied from, which doesn't make them a colour of it, or
    ///   - they're in the same Buy Mode category and have exactly the same catalog name
    ///     (that's how the game itself names its colour versions), or
    ///   - they're in the same category, cost the same, and their names only differ by a colour
    ///     word ("KARLSTAD Sofa Black" / "KARLSTAD Sofa Red") - how downloads name theirs.
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

        private static bool IsDownload(GameObject obj)
        {
            try { return obj?.Resource?.MainIff?.RuntimeInfo?.State == FSO.Files.Formats.IFF.IffRuntimeState.Standalone; }
            catch { return false; }
        }

        private static readonly System.Text.RegularExpressions.Regex ColourWords = new System.Text.RegularExpressions.Regex(
            @"\b(black|white|red|blue|green|yellow|pink|purple|violet|orange|brown|grey|gray|beige|cream|tan|teal|aqua|navy|gold|golden|silver|dark|light|pale|bright|wood|wooden|oak|pine|cherry|walnut|mahogany|maple|ebony|birch|ivory|lilac|lime|mint|peach|rose|plum|burgundy|maroon|olive|turquoise|khaki|charcoal|chrome|natural|colou?r|recolou?r|version|ver|v\d+|\d+)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>A catalog name without its colour words: "karlstad sofa black" -> "karlstad sofa".</summary>
        public static string DesignName(string name)
        {
            var n = ColourWords.Replace(name ?? "", " ");
            n = System.Text.RegularExpressions.Regex.Replace(n, @"[^a-z]+", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return System.Text.RegularExpressions.Regex.Replace(n, @"\s+", " ").Trim().ToLowerInvariant();
        }

        private static void Build(sbyte category)
        {
            BuiltCategories.Add(category);
            var content = Content.Get();
            var catalog = content.WorldCatalog;
            var items = catalog.GetItemsByCategory(category);
            var prices = new Dictionary<uint, uint>();
            var byName = new Dictionary<string, uint>();
            var byDesign = new Dictionary<string, uint>();

            foreach (var item in items)
            {
                var guid = item.GUID;
                if (prices.ContainsKey(guid)) continue;
                prices[guid] = item.Price;
                Find(guid);

                var obj = content.WorldObjects.Get(guid);
                if (obj == null) continue;

                // the game's own recolour of another of its objects in this category
                var objd = obj.OBJ;
                uint original = ((uint)objd.OriginalGUID2 << 16) | objd.OriginalGUID1;
                if (original != 0 && original != guid && !IsDownload(obj))
                {
                    var orig = catalog.GetItemByGUID(original);
                    var origObj = orig != null ? content.WorldObjects.Get(original) : null;
                    if (orig != null && orig.Value.Category == category && origObj != null && !IsDownload(origObj)) Union(guid, original);
                }

                // same name in the same category
                var name = CatalogName(obj, item);
                if (name.Length == 0) continue;
                uint first;
                if (byName.TryGetValue(name, out first)) Union(guid, first);
                else byName[name] = guid;

                // same name apart from a colour word, and the same price
                var design = DesignName(name);
                if (design.Length >= 5)
                {
                    var dkey = design + "|" + item.Price;
                    if (byDesign.TryGetValue(dkey, out first)) Union(guid, first);
                    else byDesign[dkey] = guid;
                }
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
