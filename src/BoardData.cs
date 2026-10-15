using System;

namespace ValheimWhiteboard
{
    public static class BoardData
    {
        public const int Width = 128, Height = 72, ByteCount = Width * Height / 2, Colors = 8;
        public static bool TryDecode(string encoded, out byte[] pixels)
        {
            pixels = null;
            if (encoded == null || encoded.Length != ByteCount / 3 * 4) return false;
            try { pixels = Convert.FromBase64String(encoded); }
            catch (FormatException) { return false; }
            if (pixels.Length != ByteCount) return false;
            foreach (byte value in pixels)
                if ((value & 15) >= Colors || (value >> 4) >= Colors) return false;
            return true;
        }
        public static byte Pixel(byte[] pixels, int x, int y)
        { int i = y * Width + x; return (byte)((pixels[i / 2] >> ((i & 1) * 4)) & 15); }
        public static void SetPixel(byte[] pixels, int x, int y, byte color)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height || color >= Colors) return;
            int i = y * Width + x, shift = (i & 1) * 4;
            pixels[i / 2] = (byte)((pixels[i / 2] & ~(15 << shift)) | (color << shift));
        }
        public static byte[] MergeDraft(byte[] saved, byte[] baseline, byte[] draft)
        {
            var merged = (byte[])saved.Clone();
            for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
                if (Pixel(draft, x, y) != Pixel(baseline, x, y)) SetPixel(merged, x, y, Pixel(draft, x, y));
            return merged;
        }
    }
}
