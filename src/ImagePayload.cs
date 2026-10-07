using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace DollyPaste
{
    // Images share the history's 4 MiB UTF-8 payload budget as raw PNG base64.
    internal static class ImagePayload
    {
        internal const int MaxPngBytes = 3 * 1024 * 1024;
        internal const int MaxBase64Chars = 4 * 1024 * 1024;
        internal const int MaxDimension = 8192;
        internal const int MaxPixelCount = 16 * 1024 * 1024;

        internal static bool AreDimensionsAllowed(int width, int height)
        {
            return width > 0 && height > 0 && width <= MaxDimension && height <= MaxDimension &&
                (long)width * height <= MaxPixelCount;
        }

        internal static string EncodePng(Image source)
        {
            if (source == null) return null;
            try
            {
                if (!AreDimensionsAllowed(source.Width, source.Height)) return null;
                using (BoundedPngStream stream = new BoundedPngStream())
                {
                    source.Save(stream, ImageFormat.Png);
                    return Convert.ToBase64String(stream.GetBuffer(), 0, (int)stream.Length);
                }
            }
            catch (ArgumentException) { return null; }
            catch (System.Runtime.InteropServices.ExternalException) { return null; }
            catch (OutOfMemoryException) { return null; }
            catch (IOException) { return null; }
        }

        internal static string FromPngBytes(byte[] bytes)
        {
            using (Bitmap decoded = DecodeBytes(bytes, Size.Empty))
            {
                return decoded == null ? null : Convert.ToBase64String(bytes);
            }
        }

        internal static bool IsValidBase64(string value)
        {
            using (Bitmap decoded = Decode(value)) { return decoded != null; }
        }

        internal static Bitmap Decode(string value)
        {
            return DecodeBase64(value, Size.Empty);
        }

        internal static Bitmap CreateThumbnail(string value, Size bounds)
        {
            if (!AreDimensionsAllowed(bounds.Width, bounds.Height)) return null;
            return DecodeBase64(value, bounds);
        }

        private static Bitmap DecodeBase64(string value, Size bounds)
        {
            if (string.IsNullOrEmpty(value) || value.Length > MaxBase64Chars) return null;
            try { return DecodeBytes(Convert.FromBase64String(value), bounds); }
            catch (FormatException) { return null; }
            catch (OutOfMemoryException) { return null; }
        }

        internal static bool HasValidPngHeader(byte[] bytes)
        {
            // Inspect IHDR before asking GDI+ to allocate/decompress untrusted pixels.
            if (bytes == null || bytes.Length < 33 || bytes.Length > MaxPngBytes) return false;
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
            for (int i = 0; i < signature.Length; i++)
                if (bytes[i] != signature[i]) return false;
            if (ReadBigEndian(bytes, 8) != 13 || bytes[12] != 'I' || bytes[13] != 'H' ||
                bytes[14] != 'D' || bytes[15] != 'R') return false;
            uint width = ReadBigEndian(bytes, 16);
            uint height = ReadBigEndian(bytes, 20);
            if (width > MaxDimension || height > MaxDimension ||
                !AreDimensionsAllowed((int)width, (int)height)) return false;
            return true;
        }

        private static Bitmap DecodeBytes(byte[] bytes, Size bounds)
        {
            if (!HasValidPngHeader(bytes)) return null;
            uint width = ReadBigEndian(bytes, 16);
            uint height = ReadBigEndian(bytes, 20);

            try
            {
                using (MemoryStream stream = new MemoryStream(bytes, false))
                using (Image source = Image.FromStream(stream, false, true))
                {
                    if (source.RawFormat.Guid != ImageFormat.Png.Guid || source.Width != width || source.Height != height)
                        return null;
                    int outputWidth = source.Width;
                    int outputHeight = source.Height;
                    if (!bounds.IsEmpty)
                    {
                        double scale = Math.Min(1.0, Math.Min((double)bounds.Width / outputWidth, (double)bounds.Height / outputHeight));
                        outputWidth = Math.Max(1, (int)Math.Round(outputWidth * scale));
                        outputHeight = Math.Max(1, (int)Math.Round(outputHeight * scale));
                    }
                    // Avoid compositing/rounding translucent colors when no scaling is needed.
                    if (outputWidth == source.Width && outputHeight == source.Height)
                        return ((Bitmap)source).Clone(new Rectangle(0, 0, outputWidth, outputHeight), PixelFormat.Format32bppArgb);
                    Bitmap result = new Bitmap(outputWidth, outputHeight, PixelFormat.Format32bppArgb);
                    try
                    {
                        using (Graphics graphics = Graphics.FromImage(result))
                        {
                            graphics.CompositingMode = CompositingMode.SourceCopy;
                            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            graphics.DrawImage(source, new Rectangle(0, 0, outputWidth, outputHeight),
                                0, 0, source.Width, source.Height, GraphicsUnit.Pixel);
                        }
                        return result;
                    }
                    catch { result.Dispose(); throw; }
                }
            }
            catch (ArgumentException) { return null; }
            catch (System.Runtime.InteropServices.ExternalException) { return null; }
            catch (OutOfMemoryException) { return null; }
            catch (IOException) { return null; }
        }

        private static uint ReadBigEndian(byte[] bytes, int offset)
        {
            return ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16) |
                ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];
        }

        private sealed class BoundedPngStream : MemoryStream
        {
            public override void Write(byte[] buffer, int offset, int count)
            {
                if (Position + count > MaxPngBytes) throw new IOException("Image exceeds the PNG size limit.");
                base.Write(buffer, offset, count);
            }

            public override void WriteByte(byte value)
            {
                if (Position >= MaxPngBytes) throw new IOException("Image exceeds the PNG size limit.");
                base.WriteByte(value);
            }

            public override void SetLength(long value)
            {
                if (value > MaxPngBytes) throw new IOException("Image exceeds the PNG size limit.");
                base.SetLength(value);
            }
        }
    }
}
