using System.Text;

namespace SimsRedux.Server
{
    /// <summary>
    /// A tiny read-only look inside a Sims 1 neighbourhood file (Neighborhood.iff): how many families
    /// there are and how many live in a house. Enough for the server to say whether a town has its
    /// starting families, without the game's own file code.
    ///
    /// IFF: a 64-byte header, then chunks of [type 4][size 4, big-endian, includes this 76-byte header]
    /// [id 2][flags 2][label 64][data]. A family (FAMI) starts: pad, version, "IMAF", house number.
    /// </summary>
    public static class IffPeek
    {
        public class FamilyCount
        {
            public int Families, InHouses;
            public List<int> Houses = new List<int>();
        }

        public static FamilyCount Families(string neighborhoodIff)
        {
            var result = new FamilyCount();
            byte[] d;
            try { d = File.ReadAllBytes(neighborhoodIff); }
            catch { return null; }
            if (d.Length < 64 || !Encoding.ASCII.GetString(d, 0, 8).StartsWith("IFF FILE")) return null;
            int pos = 64;
            while (pos + 76 <= d.Length)
            {
                var type = Encoding.ASCII.GetString(d, pos, 4);
                long size = ((long)d[pos + 4] << 24) | ((long)d[pos + 5] << 16) | ((long)d[pos + 6] << 8) | d[pos + 7];
                if (size < 76 || pos + size > d.Length) break;
                if (type == "FAMI" && size >= 76 + 16)
                {
                    int data = pos + 76;
                    int house = BitConverter.ToInt32(d, data + 12);
                    result.Families++;
                    if (house > 0) { result.InHouses++; result.Houses.Add(house); }
                }
                pos += (int)size;
            }
            result.Houses.Sort();
            return result;
        }

        /// <summary>"12 families, 10 living in houses" (or why it couldn't be read).</summary>
        public static string Describe(string userDataFolder)
        {
            var f = Families(Path.Combine(userDataFolder, "Neighborhood.iff"));
            if (f == null) return "the neighbourhood file couldn't be read";
            return f.Families + " famil" + (f.Families == 1 ? "y" : "ies") + ", " + f.InHouses + " living in houses"
                + (f.Houses.Count > 0 ? " (houses " + string.Join(", ", f.Houses) + ")" : "");
        }
    }
}
