using StbImageSharp;

namespace Simitone.Web
{
    /// <summary>
    /// SimsRedux web: decodes the game's pictures in C#, because the browser engine can't do it
    /// itself. The Sims 1 BMPs (8-bit palettes, sometimes RLE-compressed) are read here directly;
    /// everything else (png, jpg, gif, tga) goes through StbImageSharp.
    /// Output: RGBA bytes (not premultiplied), width, height - the same as the Windows reader.
    /// </summary>
    public static class WebImageDecoder
    {
        public static Tuple<byte[], int, int> Decode(Stream stream)
        {
            byte[] data;
            if (stream is MemoryStream ms && ms.Position == 0 && ms.TryGetBuffer(out var seg))
                data = seg.AsSpan(0, (int)ms.Length).ToArray();
            else
            {
                var copy = new MemoryStream();
                stream.CopyTo(copy);
                data = copy.ToArray();
            }

            if (data.Length > 2 && data[0] == 'B' && data[1] == 'M')
            {
                var bmp = DecodeBmp(data);
                if (bmp != null && !AllBlack(bmp.Item1)) return bmp;
                // all black usually means a BMP layout this reader gets wrong: let StbImageSharp try,
                // and say what kind of BMP it was so it can be fixed properly
                try
                {
                    var alt = ImageResult.FromMemory(data, ColorComponents.RedGreenBlueAlpha);
                    if (!AllBlack(alt.Data))
                    {
                        Console.WriteLine("[SimsRedux] image: BMP read by fallback (" + BmpInfo(data) + ")");
                        return new Tuple<byte[], int, int>(alt.Data, alt.Width, alt.Height);
                    }
                }
                catch (Exception) { }
                if (bmp != null)
                {
                    Console.WriteLine("[SimsRedux] image: BMP came out all black (" + BmpInfo(data) + ")");
                    return bmp;
                }
            }

            var img = ImageResult.FromMemory(data, ColorComponents.RedGreenBlueAlpha);
            return new Tuple<byte[], int, int>(img.Data, img.Width, img.Height);
        }

        private static bool AllBlack(byte[] rgba)
        {
            for (int i = 0; i + 3 < rgba.Length; i += 4)
                if (rgba[i] != 0 || rgba[i + 1] != 0 || rgba[i + 2] != 0) return false;
            return true;
        }

        private static string BmpInfo(byte[] d)
        {
            try
            {
                int headerSize = BitConverter.ToInt32(d, 14);
                if (headerSize == 12) return "OS/2 header, " + BitConverter.ToInt16(d, 24) + " bit";
                return "header " + headerSize + ", " + BitConverter.ToInt32(d, 18) + "x" + BitConverter.ToInt32(d, 22)
                    + ", " + BitConverter.ToInt16(d, 28) + " bit, compression " + BitConverter.ToInt32(d, 30)
                    + ", colours " + BitConverter.ToInt32(d, 46) + ", data at " + BitConverter.ToInt32(d, 10) + ", size " + d.Length;
            }
            catch (Exception) { return "unreadable header"; }
        }

        private static Tuple<byte[], int, int> DecodeBmp(byte[] d)
        {
            try
            {
                int pixelOffset = BitConverter.ToInt32(d, 10);
                int headerSize = BitConverter.ToInt32(d, 14);
                int width, height, bpp, compression, colorsUsed = 0;
                if (headerSize == 12) // old OS/2 header
                {
                    width = BitConverter.ToInt16(d, 18);
                    height = BitConverter.ToInt16(d, 20);
                    bpp = BitConverter.ToInt16(d, 24);
                    compression = 0;
                }
                else
                {
                    width = BitConverter.ToInt32(d, 18);
                    height = BitConverter.ToInt32(d, 22);
                    bpp = BitConverter.ToInt16(d, 28);
                    compression = BitConverter.ToInt32(d, 30);
                    colorsUsed = BitConverter.ToInt32(d, 46);
                }
                bool topDown = height < 0;
                height = Math.Abs(height);
                if (width <= 0 || height <= 0) return null;

                // palette (BGR(A) entries)
                int palEntry = headerSize == 12 ? 3 : 4;
                int palCount = bpp <= 8 ? (colorsUsed > 0 ? colorsUsed : 1 << bpp) : 0;
                var pal = new byte[256 * 4];
                int palStart = 14 + headerSize;
                for (int i = 0; i < palCount && i < 256; i++)
                {
                    int p = palStart + i * palEntry;
                    if (p + 2 >= d.Length) break;
                    pal[i * 4] = d[p + 2];
                    pal[i * 4 + 1] = d[p + 1];
                    pal[i * 4 + 2] = d[p];
                    pal[i * 4 + 3] = 255;
                }

                var outp = new byte[width * height * 4];
                void Put(int x, int y, byte r, byte g, byte b, byte a)
                {
                    if (x < 0 || x >= width || y < 0 || y >= height) return;
                    int row = topDown ? y : height - 1 - y;
                    int o = (row * width + x) * 4;
                    outp[o] = r; outp[o + 1] = g; outp[o + 2] = b; outp[o + 3] = a;
                }
                void PutIndex(int x, int y, int index)
                {
                    index &= 255;
                    Put(x, y, pal[index * 4], pal[index * 4 + 1], pal[index * 4 + 2], 255);
                }

                if (compression == 1 || compression == 2) // RLE8 / RLE4
                {
                    bool rle4 = compression == 2;
                    int pos = pixelOffset, x = 0, y = 0;
                    while (pos + 1 < d.Length && y < height)
                    {
                        int count = d[pos++];
                        int value = d[pos++];
                        if (count > 0)
                        {
                            for (int i = 0; i < count; i++)
                                PutIndex(x++, y, rle4 ? ((i & 1) == 0 ? value >> 4 : value & 15) : value);
                        }
                        else if (value == 0) { x = 0; y++; }          // end of line
                        else if (value == 1) break;                    // end of picture
                        else if (value == 2) { x += d[pos++]; y += d[pos++]; } // jump
                        else                                           // a run of literal pixels
                        {
                            int n = value;
                            if (rle4)
                            {
                                for (int i = 0; i < n; i++)
                                {
                                    int b = d[pos + i / 2];
                                    PutIndex(x++, y, (i & 1) == 0 ? b >> 4 : b & 15);
                                }
                                int bytes = (n + 1) / 2;
                                pos += bytes + (bytes & 1);
                            }
                            else
                            {
                                for (int i = 0; i < n; i++) PutIndex(x++, y, d[pos + i]);
                                pos += n + (n & 1);
                            }
                        }
                    }
                    return new Tuple<byte[], int, int>(outp, width, height);
                }

                if (compression != 0 && compression != 3) return null;
                int stride = ((width * bpp + 31) / 32) * 4;
                for (int y = 0; y < height; y++)
                {
                    int rowStart = pixelOffset + y * stride;
                    if (rowStart + stride > d.Length) break;
                    for (int x = 0; x < width; x++)
                    {
                        switch (bpp)
                        {
                            case 1: PutIndex(x, y, (d[rowStart + x / 8] >> (7 - (x & 7))) & 1); break;
                            case 4: PutIndex(x, y, (x & 1) == 0 ? d[rowStart + x / 2] >> 4 : d[rowStart + x / 2] & 15); break;
                            case 8: PutIndex(x, y, d[rowStart + x]); break;
                            case 16:
                                {
                                    int v = d[rowStart + x * 2] | (d[rowStart + x * 2 + 1] << 8); // 5-5-5
                                    Put(x, y, (byte)(((v >> 10) & 31) * 255 / 31), (byte)(((v >> 5) & 31) * 255 / 31), (byte)((v & 31) * 255 / 31), 255);
                                    break;
                                }
                            case 24:
                                {
                                    int p = rowStart + x * 3;
                                    Put(x, y, d[p + 2], d[p + 1], d[p], 255);
                                    break;
                                }
                            case 32:
                                {
                                    int p = rowStart + x * 4;
                                    Put(x, y, d[p + 2], d[p + 1], d[p], 255);
                                    break;
                                }
                            default: return null;
                        }
                    }
                }
                return new Tuple<byte[], int, int>(outp, width, height);
            }
            catch
            {
                return null;
            }
        }
    }
}
