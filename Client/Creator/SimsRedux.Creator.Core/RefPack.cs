namespace SimsRedux.Creator;

/// <summary>
/// EA RefPack ("QFS") compression as used by The Sims 3 packages (compression type 0xFFFF).
/// SimsRedux: header is a flags byte, 0xFB, then the uncompressed size as a 3-byte big-endian
/// number (4 bytes when flag 0x80 is set). Flag 0x01 means a compressed-size field of the same
/// width precedes the uncompressed size (older EA files); we skip it.
/// </summary>
public static class RefPack
{
    /// <summary>Reads the uncompressed size stored in a RefPack header.</summary>
    public static int ReadUncompressedSize(ReadOnlySpan<byte> src, out int headerLength)
    {
        if (src.Length < 5 || src[1] != 0xFB) throw new InvalidDataException("Not RefPack data (missing 0xFB magic).");
        byte flags = src[0];
        int sizeBytes = (flags & 0x80) != 0 ? 4 : 3;
        int p = 2;
        if ((flags & 0x01) != 0) p += sizeBytes; // skip compressed size
        if (src.Length < p + sizeBytes) throw new InvalidDataException("Truncated RefPack header.");
        long size = 0;
        for (int i = 0; i < sizeBytes; i++) size = (size << 8) | src[p + i];
        if (size > int.MaxValue) throw new InvalidDataException("RefPack size too large.");
        headerLength = p + sizeBytes;
        return (int)size;
    }

    /// <summary>
    /// Decompresses RefPack data. If <paramref name="maxOutput"/> is smaller than the full size,
    /// decoding stops as soon as at least that many bytes are available (used to peek at DDS headers).
    /// Never loops forever: every opcode consumes input, and all reads/writes are bounds-checked.
    /// </summary>
    public static byte[] Decompress(ReadOnlySpan<byte> src, int maxOutput = int.MaxValue)
    {
        int size = ReadUncompressedSize(src, out int p);
        int limit = Math.Min(size, maxOutput);
        var dst = new byte[size];
        int o = 0;
        while (o < limit)
        {
            if (p >= src.Length) throw new InvalidDataException("RefPack stream ended unexpectedly.");
            int b0 = src[p];
            int plain, copy = 0, offset = 0;
            if (b0 <= 0x7F)
            {
                Need(src, p, 2);
                int b1 = src[p + 1];
                plain = b0 & 0x03;
                copy = ((b0 & 0x1C) >> 2) + 3;
                offset = ((b0 & 0x60) << 3) + b1 + 1;
                p += 2;
            }
            else if (b0 <= 0xBF)
            {
                Need(src, p, 3);
                int b1 = src[p + 1], b2 = src[p + 2];
                plain = (b1 >> 6) & 0x03;
                copy = (b0 & 0x3F) + 4;
                offset = ((b1 & 0x3F) << 8) + b2 + 1;
                p += 3;
            }
            else if (b0 <= 0xDF)
            {
                Need(src, p, 4);
                int b1 = src[p + 1], b2 = src[p + 2], b3 = src[p + 3];
                plain = b0 & 0x03;
                copy = ((b0 & 0x0C) << 6) + b3 + 5;
                offset = ((b0 & 0x10) << 12) + (b1 << 8) + b2 + 1;
                p += 4;
            }
            else if (b0 <= 0xFB)
            {
                plain = ((b0 & 0x1F) << 2) + 4;
                p += 1;
            }
            else
            {
                // 0xFC..0xFF: final literal run of 0..3 bytes, then stop.
                plain = b0 & 0x03;
                p += 1;
                CopyPlain(src, ref p, dst, ref o, plain);
                break;
            }
            CopyPlain(src, ref p, dst, ref o, plain);
            if (copy > 0)
            {
                if (offset > o) throw new InvalidDataException("RefPack back-reference points before the start of the output.");
                if (o + copy > dst.Length) throw new InvalidDataException("RefPack output overflow.");
                int from = o - offset;
                for (int i = 0; i < copy; i++) dst[o++] = dst[from + i]; // byte-wise: overlapping copies are intended
            }
        }
        if (o < limit) throw new InvalidDataException("RefPack stream ended before the declared size.");
        if (maxOutput < size) Array.Resize(ref dst, Math.Max(o, limit));
        return dst;
    }

    private static void Need(ReadOnlySpan<byte> src, int p, int n)
    {
        if (p + n > src.Length) throw new InvalidDataException("RefPack stream ended unexpectedly.");
    }

    private static void CopyPlain(ReadOnlySpan<byte> src, ref int p, byte[] dst, ref int o, int n)
    {
        if (n == 0) return;
        if (p + n > src.Length) throw new InvalidDataException("RefPack literal run past end of input.");
        if (o + n > dst.Length) throw new InvalidDataException("RefPack output overflow.");
        src.Slice(p, n).CopyTo(dst.AsSpan(o));
        p += n; o += n;
    }

    /// <summary>
    /// Compresses data with RefPack (greedy hash-chain matcher). SimsRedux: used by tests and for
    /// writing packages; it emits every opcode form (2/3/4-byte copies, literal runs, terminator).
    /// </summary>
    public static byte[] Compress(ReadOnlySpan<byte> source, bool literalsOnly = false)
    {
        var src = source.ToArray();
        var outp = new List<byte>(src.Length / 2 + 16);
        bool big = src.Length > 0xFFFFFF;
        outp.Add((byte)(big ? 0x90 : 0x10));
        outp.Add(0xFB);
        if (big) outp.Add((byte)(src.Length >> 24));
        outp.Add((byte)(src.Length >> 16)); outp.Add((byte)(src.Length >> 8)); outp.Add((byte)src.Length);

        const int MaxOffset = 131072, HashSize = 1 << 16, MaxChain = 64;
        var head = new int[HashSize];
        Array.Fill(head, -1);
        var prev = new int[Math.Max(1, src.Length)];
        int Hash(int i) => (int)((uint)((src[i] << 16 | src[i + 1] << 8 | src[i + 2]) * 0x9E3779B1u) >> 16) & (HashSize - 1);
        void Insert(int i) { if (i + 2 < src.Length) { int h = Hash(i); prev[i] = head[h]; head[h] = i; } }

        int pos = 0, litStart = 0;
        while (pos < src.Length)
        {
            int bestLen = 0, bestOff = 0;
            if (!literalsOnly && pos + 3 <= src.Length)
            {
                int cand = head[Hash(pos)];
                int chain = 0;
                while (cand >= 0 && pos - cand <= MaxOffset && chain++ < MaxChain)
                {
                    int len = 0, maxLen = Math.Min(1028, src.Length - pos);
                    while (len < maxLen && src[cand + len] == src[pos + len]) len++;
                    int off = pos - cand;
                    bool ok = len >= 3 && (off <= 1024 || len >= 4) && (off <= 16384 || len >= 5);
                    if (ok && len > bestLen) { bestLen = len; bestOff = off; }
                    cand = prev[cand];
                }
            }
            if (bestLen == 0) { Insert(pos); pos++; continue; }

            // Flush pending literals: big runs in multiples of 4 (max 112), remainder (0..3) rides on the copy opcode.
            int lit = pos - litStart;
            while (lit > 3)
            {
                int run = Math.Min(112, lit & ~3);
                outp.Add((byte)(0xE0 + (run >> 2) - 1));
                for (int i = 0; i < run; i++) outp.Add(src[litStart + i]);
                litStart += run; lit -= run;
            }
            int plain = lit;
            int lenC = bestLen, offC = bestOff - 1;
            if (bestOff <= 1024 && lenC <= 10)
            {
                outp.Add((byte)(((offC >> 8) << 5) | ((lenC - 3) << 2) | plain));
                outp.Add((byte)offC);
            }
            else if (bestOff <= 16384 && lenC <= 67)
            {
                outp.Add((byte)(0x80 | (lenC - 4)));
                outp.Add((byte)((plain << 6) | (offC >> 8)));
                outp.Add((byte)offC);
            }
            else
            {
                if (lenC > 1028) lenC = 1028;
                int l5 = lenC - 5;
                outp.Add((byte)(0xC0 | ((offC >> 16) << 4) | ((l5 >> 8) << 2) | plain));
                outp.Add((byte)(offC >> 8));
                outp.Add((byte)offC);
                outp.Add((byte)l5);
            }
            for (int i = 0; i < plain; i++) outp.Add(src[litStart + i]);
            for (int i = 0; i < lenC; i++) Insert(pos + i);
            pos += lenC;
            litStart = pos;
        }
        int rest = src.Length - litStart;
        while (rest > 3)
        {
            int run = Math.Min(112, rest & ~3);
            outp.Add((byte)(0xE0 + (run >> 2) - 1));
            for (int i = 0; i < run; i++) outp.Add(src[litStart + i]);
            litStart += run; rest -= run;
        }
        outp.Add((byte)(0xFC | rest));
        for (int i = 0; i < rest; i++) outp.Add(src[litStart + i]);
        return outp.ToArray();
    }
}
