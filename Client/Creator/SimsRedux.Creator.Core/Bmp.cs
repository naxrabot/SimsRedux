namespace SimsRedux.Creator;

/// <summary>
/// Minimal Windows BMP support. SimsRedux: Sims 1 stores catalog thumbnails (BMP_ chunks) and floor
/// source bitmaps (FBMP chunks) as plain BMP files inside the IFF, so we need a small reader and a
/// 24-bit writer.
/// </summary>
public static class Bmp
{
    /// <summary>Decodes an uncompressed (BI_RGB) BMP with 1/4/8/24/32 bits per pixel, or 16-bit 5:5:5.</summary>
    public static Image32 Decode(byte[] data)
    {
        if (data == null || data.Length < 26 || data[0] != 'B' || data[1] != 'M') throw new InvalidDataException("Not a BMP file.");
        int pixOff = BitConverter.ToInt32(data, 10);
        int hdrSize = BitConverter.ToInt32(data, 14);
        int w, h, bpp, compression = 0, colorsUsed = 0;
        if (hdrSize == 12)
        {
            w = BitConverter.ToInt16(data, 18); h = BitConverter.ToInt16(data, 20); bpp = BitConverter.ToInt16(data, 24);
        }
        else
        {
            if (data.Length < 54) throw new InvalidDataException("Truncated BMP header.");
            w = BitConverter.ToInt32(data, 18); h = BitConverter.ToInt32(data, 22); bpp = BitConverter.ToInt16(data, 28);
            compression = BitConverter.ToInt32(data, 30); colorsUsed = BitConverter.ToInt32(data, 46);
        }
        bool topDown = h < 0; h = Math.Abs(h);
        if (w <= 0 || h <= 0 || w > 16384 || h > 16384) throw new InvalidDataException("Bad BMP size.");
        if (compression != 0 && !(compression == 3 && (bpp == 32 || bpp == 16))) throw new NotSupportedException("Compressed BMPs are not supported.");
        int palEntries = bpp <= 8 ? (colorsUsed > 0 ? colorsUsed : 1 << bpp) : 0;
        int palOff = 14 + hdrSize;
        int palStride = hdrSize == 12 ? 3 : 4;
        int stride = ((w * bpp + 31) / 32) * 4;
        if (pixOff + (long)stride * h > data.Length) throw new InvalidDataException("Truncated BMP pixel data.");
        var img = new Image32(w, h);
        for (int y = 0; y < h; y++)
        {
            int row = pixOff + (topDown ? y : h - 1 - y) * stride;
            for (int x = 0; x < w; x++)
            {
                byte r, g, b, a = 255;
                switch (bpp)
                {
                    case 1: case 4: case 8:
                        {
                            int bit = x * bpp;
                            int idx = (data[row + (bit >> 3)] >> (8 - bpp - (bit & 7))) & ((1 << bpp) - 1);
                            if (idx >= palEntries) idx = 0;
                            int p = palOff + idx * palStride;
                            b = data[p]; g = data[p + 1]; r = data[p + 2];
                            break;
                        }
                    case 16:
                        {
                            int v = BitConverter.ToUInt16(data, row + x * 2);
                            r = (byte)(((v >> 10) & 31) * 255 / 31); g = (byte)(((v >> 5) & 31) * 255 / 31); b = (byte)((v & 31) * 255 / 31);
                            break;
                        }
                    case 24:
                        b = data[row + x * 3]; g = data[row + x * 3 + 1]; r = data[row + x * 3 + 2];
                        break;
                    case 32:
                        b = data[row + x * 4]; g = data[row + x * 4 + 1]; r = data[row + x * 4 + 2];
                        break;
                    default: throw new NotSupportedException($"BMP bit depth {bpp} not supported.");
                }
                img.SetPixel(x, y, r, g, b, a);
            }
        }
        return img;
    }

    /// <summary>Encodes a 24-bit bottom-up BMP (alpha is composited over <paramref name="background"/>).</summary>
    public static byte[] Encode24(Image32 img, byte bgR = 0, byte bgG = 0, byte bgB = 0)
    {
        int stride = ((img.Width * 3) + 3) & ~3;
        int size = 54 + stride * img.Height;
        var d = new byte[size];
        d[0] = (byte)'B'; d[1] = (byte)'M';
        BitConverter.GetBytes(size).CopyTo(d, 2);
        BitConverter.GetBytes(54).CopyTo(d, 10);
        BitConverter.GetBytes(40).CopyTo(d, 14);
        BitConverter.GetBytes(img.Width).CopyTo(d, 18);
        BitConverter.GetBytes(img.Height).CopyTo(d, 22);
        BitConverter.GetBytes((short)1).CopyTo(d, 26);
        BitConverter.GetBytes((short)24).CopyTo(d, 28);
        BitConverter.GetBytes(stride * img.Height).CopyTo(d, 34);
        for (int y = 0; y < img.Height; y++)
        {
            int row = 54 + (img.Height - 1 - y) * stride;
            for (int x = 0; x < img.Width; x++)
            {
                img.GetPixel(x, y, out var r, out var g, out var b, out var a);
                d[row + x * 3] = (byte)((b * a + bgB * (255 - a)) / 255);
                d[row + x * 3 + 1] = (byte)((g * a + bgG * (255 - a)) / 255);
                d[row + x * 3 + 2] = (byte)((r * a + bgR * (255 - a)) / 255);
            }
        }
        return d;
    }

    /// <summary>Reads only the width/height of a BMP.</summary>
    public static bool TryReadSize(byte[] data, out int width, out int height)
    {
        width = height = 0;
        if (data == null || data.Length < 26 || data[0] != 'B' || data[1] != 'M') return false;
        int hdr = BitConverter.ToInt32(data, 14);
        if (hdr == 12) { width = BitConverter.ToInt16(data, 18); height = Math.Abs((int)BitConverter.ToInt16(data, 20)); }
        else { width = BitConverter.ToInt32(data, 18); height = Math.Abs(BitConverter.ToInt32(data, 22)); }
        return width > 0 && height > 0;
    }
}
