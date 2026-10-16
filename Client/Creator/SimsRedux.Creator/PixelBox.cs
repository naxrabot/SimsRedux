using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SimsRedux.Creator.App
{
    /// <summary>
    /// SimsRedux: shows a picture with crisp, square pixels (no blurring when it's made bigger), the way
    /// sprites look in the game. Fits the box at a whole-number size, or a fixed size if Zoom is set.
    /// </summary>
    public sealed class PixelBox : Control
    {
        private Image _image;

        public PixelBox()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        /// <summary>The picture (the old one is disposed when a new one is set).</summary>
        public Image Image
        {
            get => _image;
            set
            {
                var old = _image;
                _image = value;
                if (old != null && !ReferenceEquals(old, value)) old.Dispose();
                Invalidate();
            }
        }

        /// <summary>0 = fit the box; otherwise draw at exactly this many screen pixels per picture pixel.</summary>
        public int Zoom { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            if (_image == null) return;
            float s = Zoom > 0 ? Zoom : Math.Min(Width / (float)_image.Width, Height / (float)_image.Height);
            if (Zoom <= 0 && s >= 1) s = (float)Math.Floor(s); // whole numbers keep every pixel the same size
            int w = Math.Max(1, (int)(_image.Width * s)), h = Math.Max(1, (int)(_image.Height * s));
            e.Graphics.InterpolationMode = s >= 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            e.Graphics.DrawImage(_image, new Rectangle((Width - w) / 2, (Height - h) / 2, w, h));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _image?.Dispose(); _image = null; }
            base.Dispose(disposing);
        }
    }
}
