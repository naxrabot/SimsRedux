using System.Text;

namespace SimsRedux.Creator;

/// <summary>One string of a STR#/CTSS chunk.</summary>
public sealed class StrItem
{
    /// <summary>1 = English (US), 2 = English (UK), 3 = French, ... (0 is treated as 1 by readers).</summary>
    public byte LanguageCode { get; set; } = 1;
    public string Value { get; set; } = "";
    public string Comment { get; set; } = "";

    public StrItem() { }
    public StrItem(string value, string comment = "", byte lang = 1) { Value = value; Comment = comment; LanguageCode = lang; }
}

/// <summary>
/// STR# / CTSS string tables (mirrors FreeSO's STR.cs). Formats: 0x0000 Pascal strings,
/// 0xFFFF C strings, 0xFFFE string+comment pairs, 0xFFFD language code + string + comment
/// (what The Sims 1 uses and what we write), 0xFFFC (TSO, read only).
/// SimsRedux: items are kept in file order; strings of each language form their own list, so
/// "index i" means the i-th string of a language (as in FreeSO's GetString). Text is Latin-1.
/// </summary>
public sealed class StrList
{
    public short Format { get; set; } = -3;
    public List<StrItem> Items { get; } = new();

    /// <summary>Builds a format -3 table of English (US) strings with optional comments.</summary>
    public static StrList Create(IEnumerable<(string value, string comment)> strings)
    {
        var s = new StrList();
        foreach (var (v, c) in strings) s.Items.Add(new StrItem(v, c));
        return s;
    }

    public static StrList Create(params string[] values) => Create(values.Select(v => (v, "")));

    /// <summary>Strings of one language in order (language 0 counts as 1).</summary>
    public List<StrItem> Language(byte lang) => Items.Where(i => (i.LanguageCode == 0 ? 1 : i.LanguageCode) == lang).ToList();

    /// <summary>Gets string <paramref name="index"/> of a language, falling back to English (US) like FreeSO.</summary>
    public string? GetString(int index, byte lang = 1)
    {
        var l = Language(lang);
        if (l.Count == 0) l = Language(1);
        return index >= 0 && index < l.Count ? l[index].Value : null;
    }

    /// <summary>Sets string <paramref name="index"/> of a language (adding entries as needed).</summary>
    public void SetString(int index, string value, byte lang = 1)
    {
        var l = Language(lang);
        while (l.Count <= index)
        {
            var item = new StrItem("", "", lang);
            // insert after the last item of that language to keep languages grouped
            int lastPos = Items.FindLastIndex(i => (i.LanguageCode == 0 ? 1 : i.LanguageCode) == lang);
            Items.Insert(lastPos + 1, item);
            l.Add(item);
        }
        l[index].Value = value;
    }

    public static StrList Read(byte[] data)
    {
        var s = new StrList();
        if (data.Length < 2) return s;
        s.Format = BitConverter.ToInt16(data, 0);
        if (data.Length < 4) return s;
        int p = 2;
        string CStr()
        {
            int z = Array.IndexOf(data, (byte)0, p);
            if (z < 0) z = data.Length;
            var str = Encoding.Latin1.GetString(data, p, z - p);
            p = Math.Min(data.Length, z + 1);
            return str;
        }
        switch (s.Format)
        {
            case 0:
                {
                    int n = BitConverter.ToUInt16(data, p); p += 2;
                    for (int i = 0; i < n && p < data.Length; i++)
                    {
                        int len = data[p++];
                        len = Math.Min(len, data.Length - p);
                        s.Items.Add(new StrItem(Encoding.Latin1.GetString(data, p, len)));
                        p += len;
                    }
                    break;
                }
            case -1:
                {
                    int n = BitConverter.ToUInt16(data, p); p += 2;
                    for (int i = 0; i < n && p < data.Length; i++) s.Items.Add(new StrItem(CStr()));
                    break;
                }
            case -2:
                {
                    int n = BitConverter.ToUInt16(data, p); p += 2;
                    for (int i = 0; i < n && p < data.Length; i++) { var v = CStr(); var c = CStr(); s.Items.Add(new StrItem(v, c)); }
                    break;
                }
            case -3:
                {
                    int n = BitConverter.ToUInt16(data, p); p += 2;
                    for (int i = 0; i < n && p < data.Length; i++)
                    {
                        byte lang = data[p++];
                        var v = CStr(); var c = CStr();
                        s.Items.Add(new StrItem(v, c, lang));
                    }
                    break;
                }
            case -4:
                {
                    int sets = data[p++];
                    for (int set = 0; set < sets && p + 2 <= data.Length; set++)
                    {
                        int n = BitConverter.ToUInt16(data, p); p += 2;
                        for (int i = 0; i < n && p < data.Length; i++)
                        {
                            byte lang = (byte)(data[p++] + 1);
                            var v = VarPascal(data, ref p); var c = VarPascal(data, ref p);
                            s.Items.Add(new StrItem(v, c, lang));
                        }
                    }
                    break;
                }
            default:
                throw new InvalidDataException($"Unknown STR# format {s.Format}.");
        }
        return s;
    }

    private static string VarPascal(byte[] d, ref int p)
    {
        int len = 0, shift = 0;
        while (p < d.Length)
        {
            byte b = d[p++];
            len |= (b & 0x7F) << shift; shift += 7;
            if ((b & 0x80) == 0) break;
        }
        len = Math.Min(len, d.Length - p);
        var s = Encoding.UTF8.GetString(d, p, len);
        p += len;
        return s;
    }

    /// <summary>
    /// Writes format 0xFFFD (as The Sims 1 and FreeSO's TS1 writer do): int16 -3, uint16 count, then per item
    /// language byte, value\0, comment\0, and finally one 0xA3 padding byte per item (as FreeSO writes;
    /// the Maxis/frosti files have the same trailer).
    /// </summary>
    public byte[] Write()
    {
        using var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write((short)-3);
        bw.Write((ushort)Items.Count);
        foreach (var it in Items)
        {
            bw.Write(it.LanguageCode);
            bw.Write(Latin1(it.Value)); bw.Write((byte)0);
            bw.Write(Latin1(it.Comment)); bw.Write((byte)0);
        }
        for (int i = 0; i < Items.Count; i++) bw.Write((byte)0xA3);
        bw.Flush();
        return ms.ToArray();
    }

    private static byte[] Latin1(string? s)
    {
        s ??= "";
        var chars = s.Select(c => c == '\0' ? ' ' : c <= 'ÿ' ? c : '?').ToArray();
        return Encoding.Latin1.GetBytes(chars);
    }
}
