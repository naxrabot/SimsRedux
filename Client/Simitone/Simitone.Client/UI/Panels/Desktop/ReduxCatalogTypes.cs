using FSO.Client.UI.Framework;
using Microsoft.Xna.Framework;
using Simitone.Client.UI.Controls;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Client.UI.Panels.Desktop
{
    /// <summary>One kind of catalog item (Carpet, Wood, Fences...) and the words in a name that mean it.</summary>
    public class CatalogType
    {
        public string Name;
        public string[] Words;
        public CatalogType(string name, params string[] words) { Name = name; Words = words; }
    }

    /// <summary>
    /// SimsRedux: sorts Buy and Build catalog items into smaller groups by the words in their names
    /// (and their descriptions when the name says nothing). The first group that matches wins, so
    /// the lists are ordered from most specific to least.
    /// </summary>
    public static class ReduxCatalogTypes
    {
        public const string All = "All";

        // ---------------- Build mode ----------------

        public static readonly CatalogType[] Floors =
        {
            new CatalogType("Carpet", "carpet", "rug", "shag", "berber", "plush", "lush", "pile"),
            new CatalogType("Wood", "wood", "oak", "pine", "maple", "cherry", "walnut", "mahogany", "plank", "parquet", "bamboo",
                "chevron", "herringbone", "deck", "birch", "teak", "cedar", "timber", "board"),
            new CatalogType("Tile", "tile", "linoleum", "lino", "vinyl", "checker", "chequer", "ceramic", "terrazzo", "mosaic"),
            new CatalogType("Stone", "stone", "marble", "granite", "slate", "brick", "cobble", "travertine", "flagstone"),
            new CatalogType("Outdoor", "grass", "dirt", " sand", "gravel", "concrete", "asphalt", "cement", "paving", "patio",
                "lawn", "pebble", "mulch", " path", "soil", " mud", "rock"),
        };

        public static readonly CatalogType[] Wallpapers =
        {
            new CatalogType("Tile", "tile", "ceramic", "mosaic"),
            new CatalogType("Brick", "brick", "stone", "rock", "marble", "granite", "masonry", "cinder", "block", "concrete", "adobe"),
            new CatalogType("Wood", "wood", "panel", "pine", "oak", " log", "cedar", "plank", "wainscot", "cherry", "maple",
                "mahogany", "walnut", "siding", "shingle", "clapboard", "timber"),
            new CatalogType("Pattern", "wallpaper", "stripe", "floral", "flower", "pattern", "paper", "damask", "print", "plaid",
                "check", "dots", "diamond", "argyle", "paisley", "tartan"),
            new CatalogType("Paint", "paint", "solid", "plaster", "stucco", " plain", "colour", "color", "wash"),
        };

        public static readonly CatalogType[] Walls =
        {
            new CatalogType("Fences", "fence", "railing", " rail", "picket", "chain", "gate", "lattice", "hedge", "balustrade"),
        };

        public static readonly CatalogType[] Doors =
        {
            new CatalogType("Arches", "archway", " arch ", "arches"),
            new CatalogType("Gates", "gate"),
        };

        public static readonly CatalogType[] Plants =
        {
            new CatalogType("Trees", "tree", "palm", "birch", "willow", "cypress", "aspen", " elm", " oak", " pine", "spruce", "fir "),
            new CatalogType("Shrubs", "shrub", "bush", "hedge", "topiary", "boxwood", "juniper", "fern", "cactus", "grass"),
            new CatalogType("Flowers", "flower", "rose", "tulip", "daisy", "petunia", "lily", "daffodil", "geranium", "bed",
                "garden", "bloom", "iris", "poppy", "marigold", "pansy", "carnation", "sunflower", "hydrangea", "planter"),
        };

        // ---------------- Buy mode (by Sims 1 function category) ----------------

        private static readonly CatalogType[][] BuyTypes =
        {
            // 0 Seating
            new[]
            {
                new CatalogType("Sofas", "sofa", "couch", "loveseat", "settee", "divan", "futon"),
                new CatalogType("Beds", "bed", "crib", " cot ", "hammock"),
                new CatalogType("Armchair", "recliner", "armchair", "lounge", "easy chair", "beanbag", "bean bag"),
                new CatalogType("Benches", "bench", "stool", "pew"),
                new CatalogType("Chairs", "chair", "seat"),
            },
            // 1 Surfaces
            new[]
            {
                new CatalogType("Counters", "counter", "island", "bar"),
                new CatalogType("Desks", "desk"),
                new CatalogType("Side", "end table", "endtable", "nightstand", "night stand", "side table", "coffee table", "coffeetable"),
                new CatalogType("Tables", "table"),
            },
            // 2 Appliances
            new[]
            {
                new CatalogType("Fridges", "fridge", "refrigerator", "freezer", "icebox"),
                new CatalogType("Cooking", "stove", "oven", "grill", "bbq", "barbecue", "microwave", "toaster", "cooker", " range"),
                new CatalogType("Drinks", "coffee", "espresso", "juice", "drink", "soda", " tea "),
                new CatalogType("Cleaning", "dishwasher", "trash", "compactor", "washer", "dryer", "vacuum", "garbage"),
            },
            // 3 Electronics
            new[]
            {
                new CatalogType("TVs", " tv", "television", "tele", "projector"),
                new CatalogType("Computer", "computer", " pc", "laptop"),
                new CatalogType("Games", "game", "arcade", "pinball", "console", "pong"),
                new CatalogType("Music", "stereo", "radio", "jukebox", "speaker", "boombox", "sound", "music", "karaoke", " dj"),
                new CatalogType("Phones", "phone"),
                new CatalogType("Safety", "smoke", "alarm", "burglar", "sprinkler", "detector"),
            },
            // 4 Plumbing
            new[]
            {
                new CatalogType("Toilets", "toilet", "urinal", "potty"),
                new CatalogType("Baths", "shower", "tub", "bath", " spa ", "jacuzzi"),
                new CatalogType("Sinks", "sink", "basin", "fountain"),
            },
            // 5 Decorative
            new[]
            {
                new CatalogType("Mirrors", "mirror"),
                new CatalogType("Clocks", "clock"),
                new CatalogType("Rugs", "rug", "carpet", " mat"),
                new CatalogType("Curtains", "curtain", "drape", "blind"),
                new CatalogType("Plants", "plant", "flower", "fern", "palm", "cactus", "bonsai", "ivy", "orchid", "bouquet", "tree", "topiary"),
                new CatalogType("Art", "painting", "portrait", "poster", "print", "picture", "canvas", "photo", " art ", "mural", "tapestry"),
                new CatalogType("Statues", "sculpture", "statue", "bust", "figurine", "vase", "urn", "totem", "trophy"),
            },
            // 6 Miscellaneous
            new[]
            {
                new CatalogType("Bookcase", "book", "shelf", "shelves"),
                new CatalogType("Dressers", "dresser", "armoire", "wardrobe", "closet", "chest"),
                new CatalogType("Exercise", "exercise", "workout", "treadmill", "weight", "gym", "punching", "bike"),
                new CatalogType("Kids", "toy", "doll", " train", "playground", "slide", "swing", "chalk", "kid", "child", "baby"),
                new CatalogType("Hobbies", "easel", "chess", "telescope", "aquarium", "fish", "piano", "guitar", "pool table", "dart", "workbench"),
                new CatalogType("Party", "party", "buffet", "punch", "cake", "pinata", "costume"),
            },
            // 7 Lighting
            new[]
            {
                new CatalogType("Ceiling", "ceiling", "chandelier", "hanging", "pendant"),
                new CatalogType("Wall", "wall", "sconce"),
                new CatalogType("Floor", "floor", "torch", "standing"),
                new CatalogType("Table", "table", "desk", "lamp"),
                new CatalogType("Outdoor", "outdoor", "garden", "tiki", "lantern", "post", "street"),
            },
        };

        /// <summary>The Buy-mode groups for a Sims 1 function category (0 Seating ... 7 Lighting), or null.</summary>
        public static CatalogType[] ForBuyCategory(int category)
        {
            return (category >= 0 && category < BuyTypes.Length) ? BuyTypes[category] : null;
        }

        /// <summary>Which group a name (and description) belongs in; <paramref name="other"/> when none match.</summary>
        public static string TypeOf(string name, string description, CatalogType[] types, string other)
        {
            var hit = Match(Clean(name), types);
            if (hit == null && !string.IsNullOrEmpty(description)) hit = Match(Clean(description), types);
            return hit ?? other;
        }

        private static string Match(string text, CatalogType[] types)
        {
            if (text.Length <= 2) return null;
            foreach (var t in types)
                foreach (var w in t.Words)
                    if (text.Contains(w)) return t.Name;
            return null;
        }

        // lower case, punctuation to spaces, padded so " arch " can match a whole word
        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var chars = s.ToLowerInvariant().ToCharArray();
            for (int i = 0; i < chars.Length; i++) if (!char.IsLetterOrDigit(chars[i])) chars[i] = ' ';
            return " " + new string(chars) + " ";
        }

        /// <summary>
        /// The tabs to show for these items: "All", then each group that has something in it, then
        /// the leftovers. Empty when everything is in one group (no point in tabs then).
        /// </summary>
        public static List<string> Tabs(IEnumerable<string> itemTypes, CatalogType[] types, string other, int maxTabs = UIReduxTabStrip.MaxTabs)
        {
            var present = new HashSet<string>(itemTypes);
            var tabs = new List<string>();
            foreach (var t in types) if (present.Contains(t.Name) && !tabs.Contains(t.Name)) tabs.Add(t.Name);
            if (present.Contains(other) && !tabs.Contains(other)) tabs.Add(other);
            if (tabs.Count < 2) return new List<string>();
            tabs.Insert(0, All);
            if (tabs.Count > maxTabs) tabs = tabs.Take(maxTabs).ToList();
            return tabs;
        }
    }

    /// <summary>
    /// SimsRedux: two columns of small rounded tabs (All, Carpet, Wood...) to the left of a catalog's
    /// pictures. Filled top to bottom, then the second column.
    /// </summary>
    public class UIReduxTabStrip : UIContainer
    {
        public const int TabW = 54, TabH = 17, StepY = 22, Rows = 4, MaxTabs = 8;
        public const int StripWidth = TabW * 2 + 4;

        public event Action<string> OnPick;
        private List<UIReduxButton> Buttons = new List<UIReduxButton>();
        private List<string> Names;

        public UIReduxTabStrip(List<string> names, string selected)
        {
            Names = names;
            for (int i = 0; i < names.Count; i++)
            {
                var name = names[i];
                var btn = new UIReduxButton(name, TabW, TabH);
                btn.TextSize = 9;
                btn.Tab = true;
                btn.Position = new Vector2((i / Rows) * (TabW + 4), (i % Rows) * StepY);
                btn.OnButtonClick += (b) => { Select(name); OnPick?.Invoke(name); };
                Add(btn);
                Buttons.Add(btn);
            }
            Select(selected);
        }

        public void Select(string name)
        {
            for (int i = 0; i < Buttons.Count; i++) Buttons[i].Selected = Names[i] == name;
        }
    }
}
