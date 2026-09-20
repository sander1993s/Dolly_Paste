using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace DollyPaste
{
    /// <summary>
    /// Design system, color palette, typography, and vector branding for Dolly Paste.
    /// </summary>
    public static class Brand
    {
        public const string AppName = "Dolly Paste";

        // --- Color Palette ---
        // Warm wool / off-white canvas
        public static readonly Color WarmWoolCanvas = Color.FromArgb(250, 248, 245);
        public static readonly Color WarmWoolCard = Color.FromArgb(255, 255, 255);
        public static readonly Color CardSelected = Color.FromArgb(235, 243, 237);
        public static readonly Color CardHover = Color.FromArgb(244, 241, 234);

        // Dark ink left rail
        public static readonly Color DarkInkRail = Color.FromArgb(31, 36, 43);
        public static readonly Color DarkInkRailHover = Color.FromArgb(40, 47, 56);
        public static readonly Color DarkInkRailActive = Color.FromArgb(50, 59, 71);
        public static readonly Color DarkInkText = Color.FromArgb(240, 242, 245);
        public static readonly Color DarkInkTextMuted = Color.FromArgb(157, 167, 179);

        // Muted moss-green accents
        public static readonly Color MossGreen = Color.FromArgb(66, 104, 78);
        public static readonly Color MossGreenHover = Color.FromArgb(81, 124, 95);
        public static readonly Color MossGreenLight = Color.FromArgb(234, 242, 236);
        public static readonly Color MossGreenDark = Color.FromArgb(43, 71, 52);

        // Borders and dividers
        public static readonly Color Border = Color.FromArgb(226, 222, 214);
        public static readonly Color BorderLight = Color.FromArgb(238, 234, 227);

        // Content typography
        public static readonly Color TextPrimary = Color.FromArgb(32, 36, 40);
        public static readonly Color TextSecondary = Color.FromArgb(110, 118, 129);
        public static readonly Color TextMuted = Color.FromArgb(149, 157, 165);

        // Badges: Kind and Sensitive
        public static readonly Color KindTextBg = Color.FromArgb(240, 237, 230);
        public static readonly Color KindTextFg = Color.FromArgb(92, 88, 79);

        public static readonly Color KindLinkBg = Color.FromArgb(230, 242, 242);
        public static readonly Color KindLinkFg = Color.FromArgb(24, 100, 112);

        public static readonly Color KindCodeBg = Color.FromArgb(236, 233, 245);
        public static readonly Color KindCodeFg = Color.FromArgb(82, 65, 130);

        public static readonly Color SensitiveBg = Color.FromArgb(253, 235, 234);
        public static readonly Color SensitiveFg = Color.FromArgb(169, 50, 38);

        public static readonly Color WarningBannerBg = Color.FromArgb(254, 249, 231);
        public static readonly Color WarningBannerFg = Color.FromArgb(143, 102, 17);

        // --- Fonts ---
        private static readonly string FontFamilyName = "Segoe UI";
        private static readonly string CodeFontFamilyName = "Consolas";

        public static Font CreateFont(float sizePt, FontStyle style)
        {
            try
            {
                return new Font(FontFamilyName, sizePt, style);
            }
            catch
            {
                return new Font(FontFamily.GenericSansSerif, sizePt, style);
            }
        }

        public static Font CreateCodeFont(float sizePt)
        {
            try
            {
                return new Font(CodeFontFamilyName, sizePt, FontStyle.Regular);
            }
            catch
            {
                return new Font(FontFamily.GenericMonospace, sizePt, FontStyle.Regular);
            }
        }

        // --- Win32 Native Icon Management ---
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        /// <summary>
        /// Creates an application icon without leaking GDI/USER HICON handles.
        /// </summary>
        public static Icon CreateAppIcon()
        {
            using (Bitmap bmp = CreateSheepBitmap(32, 32))
            {
                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    using (Icon tempIcon = Icon.FromHandle(hIcon))
                    {
                        return (Icon)tempIcon.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
            }
        }

        /// <summary>
        /// Renders the Merino sheep into a bitmap of specified dimensions.
        /// </summary>
        public static Bitmap CreateSheepBitmap(int width, int height)
        {
            Bitmap bmp = new Bitmap(width, height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Clear(Color.Transparent);
                DrawSheep(g, new RectangleF(0, 0, width, height));
            }
            return bmp;
        }

        /// <summary>
        /// Scalable vector rendering of the original Merino ram/sheep logo.
        /// Matches the geometry in assets/sheep.svg.
        /// </summary>
        public static void DrawSheep(Graphics g, RectangleF bounds)
        {
            if (bounds.Width <= 1 || bounds.Height <= 1) return;

            SmoothingMode oldSmoothing = g.SmoothingMode;
            PixelOffsetMode oldPixelOffset = g.PixelOffsetMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // Normalized 100x100 space
            float s = Math.Min(bounds.Width, bounds.Height) / 100f;
            float ox = bounds.X + (bounds.Width - 100f * s) / 2f;
            float oy = bounds.Y + (bounds.Height - 100f * s) / 2f;

            // Helper to transform normalized coordinates
            // 4 Little sturdy legs with hooves
            using (SolidBrush backLegBrush = new SolidBrush(Color.FromArgb(46, 54, 64)))
            using (SolidBrush frontLegBrush = new SolidBrush(Color.FromArgb(32, 36, 40)))
            {
                g.FillRectangle(backLegBrush, ox + 33f * s, oy + 74f * s, 6f * s, 14f * s);
                g.FillRectangle(frontLegBrush, ox + 42f * s, oy + 76f * s, 6f * s, 13f * s);
                g.FillRectangle(frontLegBrush, ox + 52f * s, oy + 76f * s, 6f * s, 13f * s);
                g.FillRectangle(backLegBrush, ox + 61f * s, oy + 74f * s, 6f * s, 14f * s);
            }

            // Fluffy Wool Body (Overlapping rounded cloud lobes)
            using (SolidBrush woolBrush = new SolidBrush(Color.FromArgb(250, 248, 245)))
            using (Pen woolBorderPen = new Pen(Color.FromArgb(221, 216, 206), 1.5f * s))
            {
                woolBorderPen.LineJoin = LineJoin.Round;

                // Circular lobes defining fluffy wool silhouette
                RectangleF[] lobes = new RectangleF[]
                {
                    new RectangleF(ox + 20f * s, oy + 40f * s, 28f * s, 28f * s),
                    new RectangleF(ox + 52f * s, oy + 40f * s, 28f * s, 28f * s),
                    new RectangleF(ox + 27f * s, oy + 29f * s, 26f * s, 26f * s),
                    new RectangleF(ox + 47f * s, oy + 29f * s, 26f * s, 26f * s),
                    new RectangleF(ox + 24f * s, oy + 54f * s, 24f * s, 24f * s),
                    new RectangleF(ox + 52f * s, oy + 54f * s, 24f * s, 24f * s),
                    new RectangleF(ox + 36f * s, oy + 54f * s, 28f * s, 28f * s),
                    new RectangleF(ox + 33f * s, oy + 35f * s, 34f * s, 34f * s)
                };

                for (int i = 0; i < lobes.Length; i++)
                {
                    g.FillEllipse(woolBrush, lobes[i]);
                    g.DrawEllipse(woolBorderPen, lobes[i]);
                }
            }

            // Merino Ram Curled Horns (Golden amber with curled paths)
            using (Pen hornPen = new Pen(Color.FromArgb(212, 178, 118), 4.8f * s))
            using (Pen ridgePen = new Pen(Color.FromArgb(154, 120, 62), 1.2f * s))
            {
                hornPen.StartCap = LineCap.Round;
                hornPen.EndCap = LineCap.Round;
                hornPen.LineJoin = LineJoin.Round;

                // Left horn
                using (GraphicsPath leftHorn = new GraphicsPath())
                {
                    leftHorn.AddBezier(
                        ox + 40f * s, oy + 42f * s,
                        ox + 24f * s, oy + 28f * s,
                        ox + 10f * s, oy + 38f * s,
                        ox + 12f * s, oy + 52f * s);
                    leftHorn.AddBezier(
                        ox + 12f * s, oy + 52f * s,
                        ox + 14f * s, oy + 62f * s,
                        ox + 26f * s, oy + 62f * s,
                        ox + 28f * s, oy + 54f * s);
                    g.DrawPath(hornPen, leftHorn);
                }

                // Left horn ridges
                g.DrawLine(ridgePen, ox + 22f * s, oy + 38f * s, ox + 26f * s, oy + 43f * s);
                g.DrawLine(ridgePen, ox + 16f * s, oy + 48f * s, ox + 21f * s, oy + 50f * s);
                g.DrawLine(ridgePen, ox + 18f * s, oy + 56f * s, ox + 23f * s, oy + 54f * s);

                // Right horn
                using (GraphicsPath rightHorn = new GraphicsPath())
                {
                    rightHorn.AddBezier(
                        ox + 60f * s, oy + 42f * s,
                        ox + 76f * s, oy + 28f * s,
                        ox + 90f * s, oy + 38f * s,
                        ox + 88f * s, oy + 52f * s);
                    rightHorn.AddBezier(
                        ox + 88f * s, oy + 52f * s,
                        ox + 86f * s, oy + 62f * s,
                        ox + 74f * s, oy + 62f * s,
                        ox + 72f * s, oy + 54f * s);
                    g.DrawPath(hornPen, rightHorn);
                }

                // Right horn ridges
                g.DrawLine(ridgePen, ox + 78f * s, oy + 38f * s, ox + 74f * s, oy + 43f * s);
                g.DrawLine(ridgePen, ox + 84f * s, oy + 48f * s, ox + 79f * s, oy + 50f * s);
                g.DrawLine(ridgePen, ox + 82f * s, oy + 56f * s, ox + 77f * s, oy + 54f * s);
            }

            // Ears
            using (SolidBrush earBrush = new SolidBrush(Color.FromArgb(234, 219, 200)))
            using (Pen earPen = new Pen(Color.FromArgb(211, 193, 173), 1f * s))
            {
                g.FillEllipse(earBrush, ox + 28f * s, oy + 45f * s, 8f * s, 6f * s);
                g.DrawEllipse(earPen, ox + 28f * s, oy + 45f * s, 8f * s, 6f * s);

                g.FillEllipse(earBrush, ox + 64f * s, oy + 45f * s, 8f * s, 6f * s);
                g.DrawEllipse(earPen, ox + 64f * s, oy + 45f * s, 8f * s, 6f * s);
            }

            // Merino Sheep Face
            using (SolidBrush faceBrush = new SolidBrush(Color.FromArgb(234, 219, 200)))
            using (Pen facePen = new Pen(Color.FromArgb(211, 193, 173), 1.5f * s))
            {
                RectangleF faceRect = new RectangleF(ox + 37f * s, oy + 39f * s, 26f * s, 30f * s);
                g.FillEllipse(faceBrush, faceRect);
                g.DrawEllipse(facePen, faceRect);
            }

            // Head Wool Crown / Tuft
            using (SolidBrush crownBrush = new SolidBrush(Color.FromArgb(250, 248, 245)))
            using (Pen crownPen = new Pen(Color.FromArgb(221, 216, 206), 1f * s))
            {
                g.FillEllipse(crownBrush, ox + 39f * s, oy + 33f * s, 10f * s, 10f * s);
                g.DrawEllipse(crownPen, ox + 39f * s, oy + 33f * s, 10f * s, 10f * s);

                g.FillEllipse(crownBrush, ox + 51f * s, oy + 33f * s, 10f * s, 10f * s);
                g.DrawEllipse(crownPen, ox + 51f * s, oy + 33f * s, 10f * s, 10f * s);

                g.FillEllipse(crownBrush, ox + 44.5f * s, oy + 29.5f * s, 11f * s, 11f * s);
                g.DrawEllipse(crownPen, ox + 44.5f * s, oy + 29.5f * s, 11f * s, 11f * s);
            }

            // Friendly Minimalist Eyes
            using (SolidBrush eyeBrush = new SolidBrush(Color.FromArgb(32, 36, 40)))
            using (SolidBrush specBrush = new SolidBrush(Color.White))
            {
                g.FillEllipse(eyeBrush, ox + 43f * s, oy + 49.5f * s, 3.6f * s, 3.6f * s);
                g.FillEllipse(specBrush, ox + 44.5f * s, oy + 50f * s, 1.2f * s, 1.2f * s);

                g.FillEllipse(eyeBrush, ox + 53.5f * s, oy + 49.5f * s, 3.6f * s, 3.6f * s);
                g.FillEllipse(specBrush, ox + 55f * s, oy + 50f * s, 1.2f * s, 1.2f * s);
            }

            // Soft Muzzle / Nose & Mouth
            using (Pen muzzlePen = new Pen(Color.FromArgb(142, 120, 100), 1.3f * s))
            {
                muzzlePen.StartCap = LineCap.Round;
                muzzlePen.EndCap = LineCap.Round;
                g.DrawLine(muzzlePen, ox + 50f * s, oy + 56f * s, ox + 50f * s, oy + 58.5f * s);
                g.DrawLine(muzzlePen, ox + 48f * s, oy + 58.5f * s, ox + 50f * s, oy + 58.5f * s);
                g.DrawLine(muzzlePen, ox + 50f * s, oy + 58.5f * s, ox + 52f * s, oy + 58.5f * s);
            }

            g.SmoothingMode = oldSmoothing;
            g.PixelOffsetMode = oldPixelOffset;
        }

        /// <summary>
        /// Generates a GraphicsPath with rounded corners for smooth card/badge rendering.
        /// </summary>
        public static GraphicsPath CreateRoundedPath(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (rect.Width <= 0 || rect.Height <= 0) return path;

            float diameter = radius * 2f;
            if (diameter > rect.Width) diameter = rect.Width;
            if (diameter > rect.Height) diameter = rect.Height;

            RectangleF arc = new RectangleF(rect.X, rect.Y, diameter, diameter);

            // Top-left
            path.AddArc(arc, 180, 90);

            // Top-right
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);

            // Bottom-right
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);

            // Bottom-left
            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);

            path.CloseFigure();
            return path;
        }

        public static Color GetKindBadgeBg(string kind)
        {
            if (string.Equals(kind, "PROTECTED", StringComparison.OrdinalIgnoreCase)) return SensitiveBg;
            if (string.Equals(kind, "LINK", StringComparison.OrdinalIgnoreCase)) return KindLinkBg;
            if (string.Equals(kind, "CODE", StringComparison.OrdinalIgnoreCase)) return KindCodeBg;
            return KindTextBg;
        }

        public static Color GetKindBadgeFg(string kind)
        {
            if (string.Equals(kind, "PROTECTED", StringComparison.OrdinalIgnoreCase)) return SensitiveFg;
            if (string.Equals(kind, "LINK", StringComparison.OrdinalIgnoreCase)) return KindLinkFg;
            if (string.Equals(kind, "CODE", StringComparison.OrdinalIgnoreCase)) return KindCodeFg;
            return KindTextFg;
        }
    }
}
