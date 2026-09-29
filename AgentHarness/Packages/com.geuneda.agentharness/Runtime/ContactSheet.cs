using UnityEngine;

namespace Harness
{
    /// <summary>
    /// The frames of a capture sequence (G3-3) in one image an agent can read at once: a grid, left to right and top to
    /// bottom, each frame scaled down (box filter) to at most 1920 pixels of sheet width, its scenario t written above it.
    /// </summary>
    sealed class ContactSheet
    {
        const int MaxWidth = 1920;
        const int Gap = 4;
        static readonly Color32 Background = new Color32(24, 24, 24, 255);
        static readonly Color32 LabelBack = new Color32(0, 0, 0, 255);
        static readonly Color32 LabelInk = new Color32(255, 255, 255, 255);
        // 3x5 digits, rows from the top ('0'..'9', then '.').
        static readonly string[] Glyphs =
        {
            "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
            "111100111001111", "111100111101111", "111001001001001", "111101111101111", "111101111001111", "000000000000010",
        };

        readonly Color32[] m_Pixels;
        readonly int m_Cols, m_Rows, m_TileW, m_TileH, m_Scale, m_Caption;

        public int Width { get; }
        public int Height { get; }
        public string Layout => m_Cols + "x" + m_Rows;

        public ContactSheet(int frames, int frameWidth, int frameHeight)
        {
            m_Cols = Mathf.CeilToInt(Mathf.Sqrt(frames));
            m_Rows = Mathf.CeilToInt(frames / (float)m_Cols);
            m_TileW = Mathf.Max(1, Mathf.Min(frameWidth, (MaxWidth - Gap * (m_Cols + 1)) / m_Cols));
            m_TileH = Mathf.Max(1, Mathf.RoundToInt(frameHeight * (float)m_TileW / frameWidth));
            m_Scale = Mathf.Max(2, m_TileW / 160);
            m_Caption = 7 * m_Scale;   // a band above each tile for its label (the frame itself stays uncovered)
            Width = m_Cols * m_TileW + (m_Cols + 1) * Gap;
            Height = m_Rows * (m_TileH + m_Caption) + (m_Rows + 1) * Gap;
            m_Pixels = new Color32[Width * Height];
            for (var i = 0; i < m_Pixels.Length; i++) m_Pixels[i] = Background;
        }

        /// <summary>Frame <paramref name="index"/> (pixels rows bottom to top) into its tile, labeled <paramref name="label"/> (digits and '.').</summary>
        public void Add(int index, Color32[] frame, int width, int height, string label)
        {
            var x0 = Gap + index % m_Cols * (m_TileW + Gap);
            var captionTop = Height - Gap - index / m_Cols * (m_TileH + m_Caption + Gap);   // rows are bottom to top: the first row of tiles is at the top
            DrawLabel(x0, captionTop, label);
            var top = captionTop - m_Caption;
            var y0 = top - m_TileH;
            for (var ty = 0; ty < m_TileH; ty++)
            {
                var sy0 = ty * height / m_TileH;
                var sy1 = Mathf.Max(sy0 + 1, (ty + 1) * height / m_TileH);
                for (var tx = 0; tx < m_TileW; tx++)
                {
                    var sx0 = tx * width / m_TileW;
                    var sx1 = Mathf.Max(sx0 + 1, (tx + 1) * width / m_TileW);
                    int r = 0, g = 0, b = 0, n = 0;
                    for (var sy = sy0; sy < sy1; sy++)
                    {
                        var row = sy * width;
                        for (var sx = sx0; sx < sx1; sx++)
                        {
                            var c = frame[row + sx];
                            r += c.r; g += c.g; b += c.b; n++;
                        }
                    }
                    m_Pixels[(y0 + ty) * Width + x0 + tx] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);
                }
            }
        }

        void DrawLabel(int x0, int top, string text)
        {
            var s = m_Scale;
            var w = (text.Length * 4 + 1) * s;
            var h = 7 * s;
            Fill(x0, top - h, w, h, LabelBack);
            var x = x0 + s;
            foreach (var ch in text)
            {
                var g = ch == '.' ? 10 : ch - '0';
                if (g >= 0 && g < Glyphs.Length)
                {
                    for (var row = 0; row < 5; row++)
                        for (var col = 0; col < 3; col++)
                            if (Glyphs[g][row * 3 + col] == '1') Fill(x + col * s, top - s - (row + 1) * s, s, s, LabelInk);
                }
                x += 4 * s;
            }
        }

        void Fill(int x, int y, int w, int h, Color32 c)
        {
            for (var yy = Mathf.Max(0, y); yy < Mathf.Min(Height, y + h); yy++)
                for (var xx = Mathf.Max(0, x); xx < Mathf.Min(Width, x + w); xx++)
                    m_Pixels[yy * Width + xx] = c;
        }

        public void Save(string path) => HarnessCapture.WritePng(m_Pixels, Width, Height, path);
    }
}
