using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DollyPaste
{
    /// <summary>
    /// Design system, color palette, typography, and vector branding for Dolly Paste.
    /// </summary>
    public static class Brand
    {
        public const string AppName = "Dolly Paste";

        // Smet Software Solutions' website tokens (smetsoftwaresolutions.be).
        // Native controls retain the same system typography without web assets.
        public static readonly Color Canvas = Color.FromArgb(243, 247, 252);
        public static readonly Color Surface = Color.White;
        public static readonly Color HeaderBlue = Color.FromArgb(1, 82, 154);
        public static readonly Color HeaderHover = Color.FromArgb(18, 103, 191);
        public static readonly Color Accent = Color.FromArgb(26, 115, 232);
        public static readonly Color AccentHover = Color.FromArgb(21, 87, 176);
        public static readonly Color AccentSoft = Color.FromArgb(232, 240, 254);
        public static readonly Color Hero = Color.FromArgb(16, 23, 37);

        // Compatibility aliases for existing renderers and the sheep artwork.
        public static readonly Color WarmWoolCanvas = Canvas;
        public static readonly Color WarmWoolCard = Surface;
        public static readonly Color CardSelected = AccentSoft;
        public static readonly Color CardHover = Color.FromArgb(241, 243, 244);

        // Dark ink left rail
        public static readonly Color DarkInkRail = Hero;
        public static readonly Color DarkInkRailHover = Color.FromArgb(40, 47, 56);
        public static readonly Color DarkInkRailActive = Color.FromArgb(50, 59, 71);
        public static readonly Color DarkInkText = Color.FromArgb(240, 242, 245);
        public static readonly Color DarkInkTextMuted = Color.FromArgb(157, 167, 179);

        public static readonly Color MossGreen = Accent;
        public static readonly Color MossGreenHover = AccentHover;
        public static readonly Color MossGreenLight = AccentSoft;
        public static readonly Color MossGreenDark = AccentHover;

        // Borders and dividers
        public static readonly Color Border = Color.FromArgb(218, 226, 237);
        public static readonly Color BorderLight = Color.FromArgb(225, 232, 241);

        // Content typography
        public static readonly Color TextPrimary = Color.FromArgb(32, 33, 36);
        public static readonly Color TextSecondary = Color.FromArgb(95, 99, 104);
        public static readonly Color TextMuted = Color.FromArgb(112, 117, 122);

        // Badges: Kind and Sensitive
        public static readonly Color KindTextBg = Color.FromArgb(241, 243, 244);
        public static readonly Color KindTextFg = TextSecondary;

        public static readonly Color KindLinkBg = AccentSoft;
        public static readonly Color KindLinkFg = AccentHover;

        public static readonly Color KindCodeBg = Color.FromArgb(236, 233, 245);
        public static readonly Color KindCodeFg = Color.FromArgb(82, 65, 130);

        public static readonly Color SensitiveBg = Color.FromArgb(252, 232, 230);
        public static readonly Color SensitiveFg = Color.FromArgb(217, 48, 37);

        public static readonly Color WarningBannerBg = Color.FromArgb(254, 249, 231);
        public static readonly Color WarningBannerFg = Color.FromArgb(143, 102, 17);

        // --- Fonts ---
        private static readonly string FontFamilyName = "Segoe UI";
        private static readonly string CodeFontFamilyName = "Consolas";

        public static void StyleButton(Button button, bool primary)
        {
            if (button == null) throw new ArgumentNullException("button");
            button.UseVisualStyleBackColor = false;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Border;
            button.BackColor = primary ? Accent : Surface;
            button.ForeColor = primary ? Color.White : TextPrimary;
            button.FlatAppearance.MouseOverBackColor = primary ? AccentHover : CardHover;
            button.FlatAppearance.MouseDownBackColor = primary ? AccentHover : AccentSoft;
            button.Cursor = Cursors.Hand;
            button.TextAlign = ContentAlignment.MiddleCenter;
            BrandButton brandButton = button as BrandButton;
            if (brandButton != null) brandButton.SetPrimaryRole(primary);
            button.Invalidate();
        }

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

        // Small native line icons stay sharp without an image dependency or symbol-font fallback.
        public static void DrawIcon(Graphics graphics, string name, Rectangle bounds, Color color)
        {
            if (graphics == null || string.IsNullOrEmpty(name) || bounds.Width < 2 || bounds.Height < 2) return;
            GraphicsState saved = graphics.Save();
            try
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TranslateTransform(bounds.Left, bounds.Top);
                graphics.ScaleTransform(bounds.Width / 20f, bounds.Height / 20f);
                using (Pen pen = new Pen(color, 1.5f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    switch (name)
                    {
                        case "close":
                            graphics.DrawLine(pen, 5, 5, 15, 15);
                            graphics.DrawLine(pen, 15, 5, 5, 15);
                            break;
                        case "search":
                            graphics.DrawEllipse(pen, 3, 3, 10, 10);
                            graphics.DrawLine(pen, 12, 12, 17, 17);
                            break;
                        case "copy":
                            graphics.DrawRectangle(pen, 7, 7, 10, 10);
                            graphics.DrawLines(pen, new[] { new Point(4, 13), new Point(3, 13), new Point(3, 3), new Point(13, 3), new Point(13, 4) });
                            break;
                        case "pin":
                            graphics.DrawLines(pen, new[] { new Point(7, 3), new Point(13, 3), new Point(12, 8), new Point(15, 11), new Point(5, 11), new Point(8, 8), new Point(7, 3) });
                            graphics.DrawLine(pen, 10, 11, 10, 18);
                            break;
                        case "preview":
                            graphics.DrawLines(pen, new[] { new Point(11, 3), new Point(17, 3), new Point(17, 9) });
                            graphics.DrawLine(pen, 17, 3, 11, 9);
                            graphics.DrawLines(pen, new[] { new Point(9, 17), new Point(3, 17), new Point(3, 11) });
                            graphics.DrawLine(pen, 3, 17, 9, 11);
                            break;
                        case "trash":
                            graphics.DrawLine(pen, 3, 5, 17, 5);
                            graphics.DrawLines(pen, new[] { new Point(7, 5), new Point(7, 2), new Point(13, 2), new Point(13, 5) });
                            graphics.DrawLines(pen, new[] { new Point(5, 7), new Point(6, 17), new Point(14, 17), new Point(15, 7) });
                            graphics.DrawLine(pen, 8, 8, 8, 14);
                            graphics.DrawLine(pen, 12, 8, 12, 14);
                            break;
                        case "settings":
                            graphics.DrawLine(pen, 3, 6, 9, 6);
                            graphics.DrawLine(pen, 14, 6, 17, 6);
                            graphics.DrawEllipse(pen, 9, 3.5f, 5, 5);
                            graphics.DrawLine(pen, 3, 14, 6, 14);
                            graphics.DrawLine(pen, 11, 14, 17, 14);
                            graphics.DrawEllipse(pen, 6, 11.5f, 5, 5);
                            break;
                        case "pause":
                            graphics.DrawRectangle(pen, 5, 3, 3, 14);
                            graphics.DrawRectangle(pen, 12, 3, 3, 14);
                            break;
                        case "play":
                            graphics.DrawPolygon(pen, new[] { new Point(6, 3), new Point(16, 10), new Point(6, 17) });
                            break;
                        case "text":
                            graphics.DrawLine(pen, 4, 4, 16, 4);
                            graphics.DrawLine(pen, 10, 4, 10, 17);
                            graphics.DrawLine(pen, 7, 17, 13, 17);
                            break;
                        case "link":
                            graphics.DrawArc(pen, 3, 9, 8, 8, 45, 270);
                            graphics.DrawArc(pen, 9, 3, 8, 8, 225, 270);
                            graphics.DrawLine(pen, 7, 13, 13, 7);
                            break;
                        case "code":
                            graphics.DrawLines(pen, new[] { new Point(6, 5), new Point(2, 10), new Point(6, 15) });
                            graphics.DrawLines(pen, new[] { new Point(14, 5), new Point(18, 10), new Point(14, 15) });
                            break;
                        case "image":
                            graphics.DrawRectangle(pen, 2, 3, 16, 14);
                            graphics.DrawEllipse(pen, 5, 6, 3, 3);
                            graphics.DrawLines(pen, new[] { new Point(3, 16), new Point(9, 10), new Point(13, 14), new Point(16, 11), new Point(18, 13) });
                            break;
                        case "binary":
                            graphics.DrawRectangle(pen, 3, 3, 4, 5);
                            graphics.DrawLine(pen, 14, 3, 14, 8);
                            graphics.DrawLine(pen, 5, 12, 5, 17);
                            graphics.DrawRectangle(pen, 12, 12, 4, 5);
                            break;
                    }
                }
            }
            finally { graphics.Restore(saved); }
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

    // A native Button keeps its keyboard, default-button and accessibility
    // behavior; only its rendering changes. No timers, bitmaps or per-paint fonts.
    public sealed class BrandButton : Button
    {
        private bool _hovered;
        private bool _pressed;
        private bool _primary;
        private string _iconName;
        private bool _hideText;
        private float _cornerRadius = 8f;

        public string IconName
        {
            get { return _iconName; }
            set { _iconName = value; Invalidate(); }
        }

        // Keep Text available to accessibility, keyboard mnemonics, and automation.
        public bool HideText
        {
            get { return _hideText; }
            set { _hideText = value; Invalidate(); }
        }

        public float CornerRadius
        {
            get { return _cornerRadius; }
            set { _cornerRadius = Math.Max(1f, value); Invalidate(); }
        }

        public BrandButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Brand.StyleButton(this, false);
        }

        public bool IsPrimary
        {
            get { return _primary; }
            set { Brand.StyleButton(this, value); }
        }

        internal void SetPrimaryRole(bool primary)
        {
            _primary = primary;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color parentColor = Parent != null ? Parent.BackColor : Brand.Surface;
            if (parentColor.A == 0) parentColor = Brand.Surface;
            e.Graphics.Clear(parentColor);
            if (Width < 2 || Height < 2) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Color fill = !Enabled ? Brand.CardHover :
                (_pressed ? FlatAppearance.MouseDownBackColor :
                (_hovered ? FlatAppearance.MouseOverBackColor : BackColor));
            Color text = Enabled ? ForeColor : Brand.TextMuted;
            RectangleF bounds = new RectangleF(0.5f, 0.5f, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            using (GraphicsPath path = Brand.CreateRoundedPath(bounds, Math.Min(_cornerRadius, Height / 2f)))
            using (SolidBrush brush = new SolidBrush(fill))
            {
                e.Graphics.FillPath(brush, path);
                if (FlatAppearance.BorderSize > 0)
                {
                    using (Pen pen = new Pen(Enabled ? FlatAppearance.BorderColor : Brand.Border, 1))
                        e.Graphics.DrawPath(pen, path);
                }
            }

            Rectangle textBounds = new Rectangle(5, 2, Math.Max(0, Width - 10), Math.Max(0, Height - 4));
            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            if (!UseMnemonic) flags |= TextFormatFlags.NoPrefix;
            else if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
            if (!string.IsNullOrEmpty(_iconName))
            {
                int iconSize = Math.Max(1, Math.Min(16, Math.Min(Width - 8, Height - 8)));
                int iconLeft = (Width - iconSize) / 2;
                if (!_hideText && !string.IsNullOrEmpty(Text))
                {
                    int textWidth = TextRenderer.MeasureText(e.Graphics, Text, Font, Size.Empty,
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
                    int groupWidth = Math.Min(Math.Max(1, Width - 14), iconSize + 6 + textWidth);
                    iconLeft = Math.Max(5, (Width - groupWidth) / 2);
                    textBounds.X = iconLeft + iconSize + 6;
                    textBounds.Width = Math.Max(0, Width - textBounds.X - 7);
                    flags &= ~TextFormatFlags.HorizontalCenter;
                    flags |= TextFormatFlags.Left;
                }
                Brand.DrawIcon(e.Graphics, _iconName, new Rectangle(iconLeft, (Height - iconSize) / 2, iconSize, iconSize), text);
            }
            if (!_hideText) TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, text, flags);
            if (Focused && ShowFocusCues)
            {
                Rectangle focus = new Rectangle(7, 5, Math.Max(0, Width - 14), Math.Max(0, Height - 10));
                ControlPaint.DrawFocusRectangle(e.Graphics, focus, text, fill);
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) _pressed = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) _pressed = true;
            Invalidate();
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            _pressed = false;
            Invalidate();
            base.OnKeyUp(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            _pressed = false;
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            if (!Enabled) { _hovered = false; _pressed = false; }
            Invalidate();
            base.OnEnabledChanged(e);
        }
    }
}
