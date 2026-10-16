using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace SimsRedux.Creator.App
{
    /// <summary>SimsRedux: moving pictures between the Creator's Image32 and Windows bitmaps.</summary>
    public static class Pictures
    {
        public static Bitmap ToBitmap(Image32 img)
        {
            var bmp = new Bitmap(Math.Max(1, img.Width), Math.Max(1, img.Height), PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new byte[bmp.Width * 4];
                for (int y = 0; y < img.Height; y++)
                {
                    for (int x = 0; x < img.Width; x++)
                    {
                        int s = (y * img.Width + x) * 4, d = x * 4;
                        row[d] = img.Rgba[s + 2];     // B
                        row[d + 1] = img.Rgba[s + 1]; // G
                        row[d + 2] = img.Rgba[s];     // R
                        row[d + 3] = img.Rgba[s + 3]; // A
                    }
                    Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, row.Length);
                }
            }
            finally { bmp.UnlockBits(data); }
            return bmp;
        }

        public static Image32 FromBitmap(Bitmap src)
        {
            using (var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp)) g.DrawImage(src, 0, 0, src.Width, src.Height);
                var img = new Image32(bmp.Width, bmp.Height);
                var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var row = new byte[bmp.Width * 4];
                    for (int y = 0; y < bmp.Height; y++)
                    {
                        Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                        for (int x = 0; x < bmp.Width; x++)
                        {
                            int d = (y * bmp.Width + x) * 4, s = x * 4;
                            img.Rgba[d] = row[s + 2];
                            img.Rgba[d + 1] = row[s + 1];
                            img.Rgba[d + 2] = row[s];
                            img.Rgba[d + 3] = row[s + 3];
                        }
                    }
                }
                finally { bmp.UnlockBits(data); }
                return img;
            }
        }

        /// <summary>A picture file from disk: PNG, JPG, BMP, GIF (via Windows) or DDS (via the Creator).</summary>
        public static Image32 LoadFile(string path)
        {
            var bytes = File.ReadAllBytes(path);
            if (path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)) return Dds.Decode(bytes);
            using (var ms = new MemoryStream(bytes))
            using (var bmp = new Bitmap(ms))
                return FromBitmap(bmp);
        }

        /// <summary>A square thumbnail (letterboxed on a dark background).</summary>
        public static Bitmap Thumbnail(Image32 img, int size)
        {
            var thumb = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var src = ToBitmap(img))
            using (var g = Graphics.FromImage(thumb))
            {
                g.Clear(Color.FromArgb(30, 34, 52));
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                float s = Math.Min(size / (float)Math.Max(1, img.Width), size / (float)Math.Max(1, img.Height));
                int w = Math.Max(1, (int)(img.Width * s)), h = Math.Max(1, (int)(img.Height * s));
                g.DrawImage(src, (size - w) / 2, (size - h) / 2, w, h);
            }
            return thumb;
        }
    }
}
