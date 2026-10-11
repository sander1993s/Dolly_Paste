using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DollyPaste
{
    public enum NavMode
    {
        All,
        Pinned
    }

    internal enum ImageCopyTarget { Image, OcrText, Base64 }

    internal sealed class ClipListBox : ListBox
    {
        internal ClipListBox() { DoubleBuffered = true; }
    }

    /// <summary>
    /// Text box with customizable clipboard behavior to enforce Dolly Paste privacy contracts.
    /// In demo/preview modes, all native clipboard operations (WM_COPY, WM_CUT, WM_PASTE) are suppressed.
    /// In normal mode, preview box suppresses native copy and routes Ctrl+C to CopySelectedClip.
    /// Search routes copy to the selected clip unless the user has selected search text.
    /// </summary>
    internal class PrivacyTextBox : TextBox
    {
        private readonly bool _disableNativeClipboard;
        private readonly Action _onRoutedCopy;
        private readonly bool _preferTextSelection;

        public PrivacyTextBox(bool disableNativeClipboard, Action onRoutedCopy, bool preferTextSelection = false)
        {
            _disableNativeClipboard = disableNativeClipboard;
            _onRoutedCopy = onRoutedCopy;
            _preferTextSelection = preferTextSelection;

            if (_disableNativeClipboard)
            {
                this.ShortcutsEnabled = false;
                this.ContextMenu = new ContextMenu();
            }
        }

        private bool ShouldRouteCopy
        {
            get { return _onRoutedCopy != null && (!_preferTextSelection || SelectionLength == 0); }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.C))
            {
                if (ShouldRouteCopy)
                {
                    _onRoutedCopy();
                    return true;
                }
                if (_disableNativeClipboard)
                {
                    return true;
                }
            }

            if (_disableNativeClipboard)
            {
                if (keyData == (Keys.Control | Keys.V) ||
                    keyData == (Keys.Control | Keys.X) ||
                    keyData == (Keys.Shift | Keys.Insert) ||
                    keyData == (Keys.Control | Keys.Insert) ||
                    keyData == (Keys.Shift | Keys.Delete))
                {
                    return true;
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_CUT = 0x0300;
            const int WM_COPY = 0x0301;
            const int WM_PASTE = 0x0302;

            if (m.Msg == WM_COPY)
            {
                if (ShouldRouteCopy)
                {
                    _onRoutedCopy();
                    return;
                }
                if (_disableNativeClipboard)
                {
                    return;
                }
            }

            if (_disableNativeClipboard)
            {
                if (m.Msg == WM_CUT || m.Msg == WM_PASTE)
                {
                    return;
                }
            }

            base.WndProc(ref m);
        }
    }

    /// <summary>
    /// Compact clipboard picker with search, keyboard selection, and an optional preview.
    /// </summary>
    public sealed class MainForm : Form
    {
        public const string ShowMessageName = "DollyPaste_ShowInstance_Message_2026";

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        public static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);

        private readonly HistoryStore _history;
        private WindowsClipboard _clipboard;
        private readonly string _dataDirectory;
        private readonly bool _isDemoMode;
        private readonly bool _isPreviewMode;
        private readonly string _previewOutputPath;

        private bool _isQuitting;
        private bool _isDisposed;
        private bool _isRefreshing;
        private readonly uint _wmShowInstance;
        private Icon _formIcon;

        private List<ClipEntry> _allClips;
        private List<ClipEntry> _displayedClips;
        private Guid? _selectedClipId;
        private ImageCopyTarget _imageCopyTarget = ImageCopyTarget.Image;
        private bool _changingListItems;
        private NavMode _currentNav;
        private string _currentKindFilter;

        // UI Panels and Controls
        private Button _btnPreview;
        private bool _settingsOpen;
        private bool _previewExpanded;
        private ToolTip _toolTip;
        private Button _btnNavAll;
        private Button _btnNavPinned;
        private Button _btnSettings;

        private Panel _pnlMain;
        private Panel _pnlDemoBanner;
        private Panel _pnlHeader;
        private Label _lblHeaderTitle;
        private Button _btnClose;
        private Button _btnPauseResume;

        private Panel _pnlSearchFilter;
        private Panel _pnlSearchBox;
        private Panel _pnlTypeFilters;
        private Label _lblClipCount;
        private Label _lblFormatHint;
        private TextBox _txtSearch;
        private Button _btnFilterAll;
        private Button _btnFilterText;
        private Button _btnFilterLink;
        private Button _btnFilterCode;
        private Button _btnFilterImage;

        private Panel _pnlContent;
        private Panel _pnlListContainer;
        private ListBox _listBoxClips;
        private Panel _pnlEmptyState;
        private Label _lblEmptyTitle;
        private Label _lblEmptySubtitle;

        private Panel _pnlDetail;
        private Panel _pnlDetailEmpty;
        private Panel _pnlDetailContent;
        private Panel _pnlDetailToolbar;
        private Panel _pnlDetailMeta;
        private Panel _pnlTextContainer;
        private Label _lblDetailKind;
        private Label _lblDetailTime;
        private Button _btnCopy;
        private Button _btnPin;
        private Button _btnDelete;
        private Label _lblCharCount;
        private TextBox _txtPreview;
        private Panel _pnlImagePreview;
        private PictureBox _picImagePreview;
        private Panel _pnlOcrPreview;
        private TextBox _txtOcrPreview;
        private Panel _pnlImageActions;
        private Button _btnCopyImageBase64;
        private Button _btnCopyImageText;
        private Guid? _previewImageId;
        private readonly Dictionary<Guid, Bitmap> _imageThumbnails = new Dictionary<Guid, Bitmap>();
        private const int MaxCachedImageThumbnails = 64;

        private Panel _pnlStatusBar;
        private Label _lblStatusLeft;
        private Label _lblStatusRight;
        private Label _lblToast;

        private NotifyIcon _notifyIcon;
        private ContextMenu _trayMenu;
        private MenuItem _trayItemPause;

        private Timer _pruneTimer;
        private Timer _statusTimer;
        private Timer _toastTimer;
        private Timer _previewTimer;

        // Cached UI and Preview Fonts (reused and disposed cleanly)
        private Font _fontPreviewRegular;
        private Font _fontPreviewCode;
        private Font _fontPreviewItalic;
        private Font _fontHeaderTitle;
        private Font _fontNavBold;
        private Font _fontRegular90;
        private Font _fontBold90;
        private Font _fontRegular85;
        private Font _fontBold85;
        private Font _fontRegular80;
        private Font _fontBold80;
        private Font _fontBold75;

        private bool _previewRenderSucceeded;
        private string _previewRenderError;

        internal bool PreviewRenderSucceeded { get { return _previewRenderSucceeded; } }
        internal string PreviewRenderError { get { return _previewRenderError; } }

        public MainForm(
            HistoryStore history,
            WindowsClipboard clipboard,
            string dataDirectory,
            bool isDemoMode,
            bool isPreviewMode,
            string previewOutputPath)
        {
            if (history == null) throw new ArgumentNullException("history");

            _history = history;
            _clipboard = clipboard;
            _dataDirectory = dataDirectory;
            _isDemoMode = isDemoMode;
            _isPreviewMode = isPreviewMode;
            _previewOutputPath = previewOutputPath;

            _allClips = new List<ClipEntry>();
            _displayedClips = new List<ClipEntry>();
            _currentNav = NavMode.All;
            _currentKindFilter = null;

            _wmShowInstance = RegisterWindowMessage(ShowMessageName);

            InitializeFonts();
            InitializeForm();
            InitializeComponent();
            InitializeTray();
            InitializeTimers();

            _history.Changed += OnHistoryChanged;

            // Load initial snapshot
            RefreshClipsList();

            // If preview mode, schedule layout capture
            if (_isPreviewMode)
            {
                _previewTimer = new Timer();
                _previewTimer.Interval = 300;
                _previewTimer.Tick += OnPreviewTimerTick;
                _previewTimer.Start();
            }
        }

        private void InitializeFonts()
        {
            _fontPreviewRegular = Brand.CreateFont(9.5f, FontStyle.Regular);
            _fontPreviewCode = Brand.CreateCodeFont(9.5f);
            _fontPreviewItalic = Brand.CreateFont(9.0f, FontStyle.Italic);

            _fontHeaderTitle = Brand.CreateFont(10.5f, FontStyle.Bold);
            _fontNavBold = Brand.CreateFont(9.5f, FontStyle.Bold);
            _fontRegular90 = Brand.CreateFont(9.0f, FontStyle.Regular);
            _fontBold90 = Brand.CreateFont(9.0f, FontStyle.Bold);
            _fontRegular85 = Brand.CreateFont(8.5f, FontStyle.Regular);
            _fontBold85 = Brand.CreateFont(8.5f, FontStyle.Bold);
            _fontRegular80 = Brand.CreateFont(8.0f, FontStyle.Regular);
            _fontBold80 = Brand.CreateFont(8.0f, FontStyle.Bold);
            _fontBold75 = Brand.CreateFont(7.5f, FontStyle.Bold);
        }

        public void SetClipboard(WindowsClipboard clipboard)
        {
            _clipboard = clipboard;
            UpdatePauseResumeState();
            UpdateStatusBar();
        }

        internal static void ClampWindowSize(
            Rectangle workingArea,
            Size targetSize,
            Size targetMinSize,
            out Size initialSize,
            out Size minSize)
        {
            int maxW = Math.Max(1, workingArea.Width - 16);
            int maxH = Math.Max(1, workingArea.Height - 32);

            int initW = Math.Min(targetSize.Width, maxW);
            int initH = Math.Min(targetSize.Height, maxH);

            int minW = Math.Min(targetMinSize.Width, initW);
            int minH = Math.Min(targetMinSize.Height, initH);

            initialSize = new Size(initW, initH);
            minSize = new Size(minW, minH);
        }

        private void InitializeForm()
        {
            this.AutoScaleMode = AutoScaleMode.None;
            this.Text = "Dolly Paste";
            this.FormBorderStyle = FormBorderStyle.None;
            // Reserve a narrow, uncovered edge for native resize hit testing.
            this.Padding = new Padding(5);
            this.ShowInTaskbar = false;
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.DoubleBuffered = true;

            Screen screen = Screen.PrimaryScreen;
            try
            {
                screen = Screen.FromPoint(Cursor.Position) ?? Screen.PrimaryScreen;
            }
            catch
            {
            }
            Rectangle workArea = (screen != null) ? screen.WorkingArea : Screen.PrimaryScreen.WorkingArea;
            Size initialSize, minSize;
            ClampWindowSize(workArea, new Size(480, 520), new Size(420, 460), out initialSize, out minSize);
            this.Size = initialSize;
            this.MinimumSize = minSize;

            this.StartPosition = FormStartPosition.Manual;
            this.Bounds = GetPopupBounds(Screen.FromPoint(Cursor.Position).WorkingArea, Cursor.Position, this.Size);
            this.BackColor = Brand.WarmWoolCanvas;
            _formIcon = Brand.CreateAppIcon();
            this.Icon = _formIcon;
            this.KeyPreview = true;
        }

        private void InitializeComponent()
        {
            SuspendLayout();
            _toolTip = new ToolTip();
            _pnlMain = new Panel { Dock = DockStyle.Fill, BackColor = Brand.Canvas };
            _pnlHeader = new Panel { Dock = DockStyle.Top, Height = 41, BackColor = Brand.HeaderBlue };
            _pnlHeader.Paint += delegate(object sender, PaintEventArgs e)
            {
                Brand.DrawSheep(e.Graphics, HeaderSheepBounds);
            };
            _pnlHeader.MouseDown += OnTitleBarMouseDown;
            _lblHeaderTitle = new Label { Text = "Dolly Paste", Font = _fontHeaderTitle,
                ForeColor = Color.White, Location = new Point(47, 9), AutoSize = true };
            _lblHeaderTitle.MouseDown += OnTitleBarMouseDown;
            _btnClose = CreateActionButton("Close", 32, delegate { Close(); });
            ConfigureIconButton(_btnClose, "close", true);
            _btnClose.BackColor = Brand.HeaderBlue;
            _btnClose.ForeColor = Color.White;
            _btnClose.FlatAppearance.MouseOverBackColor = Brand.HeaderHover;
            _btnClose.FlatAppearance.MouseDownBackColor = Brand.HeaderHover;
            _btnClose.AccessibleName = "Close window";
            _toolTip.SetToolTip(_btnClose, "Hide to tray (Esc)");
            _pnlHeader.Controls.AddRange(new Control[] { _lblHeaderTitle, _btnClose });
            _pnlHeader.Resize += delegate { UpdateHeaderLayout(); };

            _pnlSearchFilter = new Panel { Dock = DockStyle.Top, Height = 51, BackColor = Brand.HeaderBlue };
            _pnlSearchBox = new Panel { Height = 36, BackColor = Brand.HeaderBlue };
            _pnlSearchBox.Paint += delegate(object sender, PaintEventArgs e)
            {
                DrawRoundedCard(e.Graphics, _pnlSearchBox.ClientRectangle, Brand.Surface,
                    _txtSearch.Focused ? Brand.AccentSoft : Brand.Surface, false);
                Brand.DrawIcon(e.Graphics, "search", new Rectangle(10, 10, 16, 16), Brand.TextMuted);
            };
            _pnlSearchBox.Click += delegate { _txtSearch.Focus(); };
            _txtSearch = new PrivacyTextBox(_isDemoMode || _isPreviewMode, delegate { CopySelectedClip(); }, true);
            _txtSearch.BorderStyle = BorderStyle.None;
            _txtSearch.BackColor = Brand.Surface;
            _txtSearch.ForeColor = Brand.TextPrimary;
            _txtSearch.Font = _fontPreviewRegular;
            _txtSearch.AccessibleName = "Search clips";
            _txtSearch.TextChanged += delegate { ApplyFilters(); };
            _txtSearch.GotFocus += delegate { _pnlSearchBox.Invalidate(); };
            _txtSearch.LostFocus += delegate { _pnlSearchBox.Invalidate(); };
            _txtSearch.HandleCreated += delegate
            {
                SendMessage(_txtSearch.Handle, 0x1501, new IntPtr(1), "Search clipboard...");
            };
            _toolTip.SetToolTip(_txtSearch, "Search clipboard history (Ctrl+F)");
            _pnlSearchBox.Controls.Add(_txtSearch);
            _pnlSearchFilter.Controls.Add(_pnlSearchBox);
            _pnlSearchFilter.Resize += delegate { UpdateSearchFilterLayout(); };

            _btnNavAll = CreateActionButton("All", 42, delegate
            {
                _currentNav = NavMode.All;
                UpdateNavStyles();
                ApplyFilters();
            });
            _btnNavPinned = CreateActionButton("Pinned", 60, delegate
            {
                _currentNav = NavMode.Pinned;
                UpdateNavStyles();
                ApplyFilters();
            });
            _pnlSearchFilter.Controls.AddRange(new Control[] { _btnNavAll, _btnNavPinned });
            _pnlTypeFilters = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Brand.Surface };
            _pnlTypeFilters.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Brand.BorderLight))
                    e.Graphics.DrawLine(pen, 0, _pnlTypeFilters.Height - 1, _pnlTypeFilters.Width, _pnlTypeFilters.Height - 1);
            };
            int filterX = 9;
            _btnFilterAll = CreateFilterPill("Any", null, 44, ref filterX);
            _btnFilterText = CreateFilterPill("Text", "TEXT", 46, ref filterX);
            _btnFilterLink = CreateFilterPill("Links", "LINK", 49, ref filterX);
            _btnFilterCode = CreateFilterPill("Code", "CODE", 49, ref filterX);
            _btnFilterImage = CreateFilterPill("Images", "IMAGE", 63, ref filterX);
            _lblClipCount = new Label { Font = _fontRegular80, ForeColor = Brand.TextMuted,
                TextAlign = ContentAlignment.MiddleRight, Anchor = AnchorStyles.Right | AnchorStyles.Top };
            _pnlTypeFilters.Controls.Add(_lblClipCount);
            _pnlTypeFilters.Resize += delegate
            {
                _lblClipCount.SetBounds(_pnlTypeFilters.Width - 90, 5, 76, 30);
            };
            _toolTip.SetToolTip(_btnFilterAll, "Show every content type");

            _pnlContent = new Panel { Dock = DockStyle.Fill, BackColor = Brand.Canvas };
            InitializeDetailPane();
            _pnlDetail.Dock = DockStyle.Bottom;
            _pnlDetail.Height = 168;
            _pnlDetail.Visible = false;
            _pnlDetailToolbar.Parent = _pnlMain;
            _pnlDetailToolbar.Dock = DockStyle.Bottom;
            _pnlDetailToolbar.Height = 52;
            _btnCopy.Text = "Copy text";
            ((BrandButton)_btnCopy).IconName = "copy";
            _btnCopy.BackColor = Brand.HeaderBlue;
            _btnCopy.FlatAppearance.MouseOverBackColor = Brand.HeaderHover;
            _btnCopy.FlatAppearance.MouseDownBackColor = Brand.HeaderHover;
            _btnCopy.SetBounds(9, 9, 130, 34);
            ConfigureIconButton(_btnPin, "pin", true);
            ConfigureIconButton(_btnDelete, "trash", true);
            _btnPin.SetBounds(145, 9, 34, 34);
            _btnDelete.SetBounds(221, 9, 34, 34);
            _btnPreview = CreateActionButton("Preview", 34, delegate { TogglePreview(); });
            ConfigureIconButton(_btnPreview, "preview", true);
            _btnPreview.SetBounds(183, 9, 34, 34);
            _btnPreview.AccessibleName = "Toggle clip preview";
            _lblFormatHint = new Label { Font = _fontRegular80, ForeColor = Brand.TextMuted,
                TextAlign = ContentAlignment.MiddleRight, Text = "← → format", Visible = false };
            _pnlDetailToolbar.Controls.AddRange(new Control[] { _btnPreview, _lblFormatHint });
            _pnlDetailToolbar.Resize += delegate
            {
                _lblFormatHint.SetBounds(Math.Max(260, _pnlDetailToolbar.Width - 108), 9,
                    Math.Max(0, Math.Min(94, _pnlDetailToolbar.Width - 274)), 34);
            };
            _toolTip.SetToolTip(_btnPreview, "Show or hide preview (Ctrl+Space)");
            _toolTip.SetToolTip(_btnPin, "Pin or unpin selected clip (Ctrl+P)");
            _toolTip.SetToolTip(_btnDelete, "Delete selected clip (Delete)");
            _toolTip.SetToolTip(_btnCopy, "Copy selected clip and close (Enter or Ctrl+C)");

            _pnlListContainer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 4), BackColor = Brand.Canvas };
            _listBoxClips = new ClipListBox { Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawVariable,
                ItemHeight = 42, BorderStyle = BorderStyle.None, BackColor = Brand.Canvas,
                IntegralHeight = false, AccessibleName = "Clipboard history items" };
            _listBoxClips.MeasureItem += OnListBoxMeasureItem;
            _listBoxClips.DrawItem += OnListBoxDrawItem;
            _listBoxClips.SelectedIndexChanged += OnListBoxSelectedIndexChanged;
            _listBoxClips.KeyDown += OnListBoxKeyDown;
            _listBoxClips.MouseDown += OnClipsMouseDown;
            _listBoxClips.DoubleClick += delegate { CopySelectedClip(); };
            _toolTip.SetToolTip(_listBoxClips, "Up/Down selects a clip. For images, Left/Right selects Image, Text or Base64. Enter copies.");
            _pnlListContainer.Controls.Add(_listBoxClips);

            _pnlEmptyState = new Panel { Dock = DockStyle.Fill, BackColor = Brand.Canvas, Visible = false };
            _lblEmptyTitle = new Label { Font = _fontHeaderTitle, ForeColor = Brand.TextPrimary,
                TextAlign = ContentAlignment.MiddleCenter };
            _lblEmptySubtitle = new Label { Font = _fontRegular90, ForeColor = Brand.TextSecondary,
                TextAlign = ContentAlignment.MiddleCenter };
            _pnlEmptyState.Controls.AddRange(new Control[] { _lblEmptyTitle, _lblEmptySubtitle });
            _pnlEmptyState.Resize += delegate { UpdateEmptyStateLayout(); };
            _pnlListContainer.Controls.Add(_pnlEmptyState);
            _pnlContent.Controls.Add(_pnlListContainer);
            _pnlContent.Controls.Add(_pnlDetail);
            _pnlContent.Resize += delegate { UpdatePreviewHeight(); };

            _pnlStatusBar = new Panel { Dock = DockStyle.Bottom, Height = 31, BackColor = Brand.Surface };
            _lblStatusRight = new Label { Font = _fontRegular80, ForeColor = Brand.TextMuted,
                TextAlign = ContentAlignment.MiddleLeft };
            _lblStatusLeft = new Label { Font = _fontRegular80, ForeColor = Brand.TextMuted,
                AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
            _lblToast = new Label { Font = _fontBold80, ForeColor = Brand.HeaderBlue,
                AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Visible = false };
            _btnPauseResume = CreateActionButton("Pause", 80, OnPauseResumeClicked);
            ConfigureIconButton(_btnPauseResume, "pause", false);
            _btnPauseResume.AccessibleName = "Toggle pause recording";
            _btnSettings = CreateActionButton("Settings", 30, delegate { OpenSettings(); });
            ConfigureIconButton(_btnSettings, "settings", true);
            _btnSettings.AccessibleName = "Open settings dialog";
            _toolTip.SetToolTip(_btnPauseResume, "Pause or resume clipboard recording");
            _toolTip.SetToolTip(_btnSettings, "Settings");
            _pnlStatusBar.Controls.AddRange(new Control[] { _lblStatusLeft, _lblToast, _lblStatusRight, _btnPauseResume, _btnSettings });
            _pnlStatusBar.Resize += delegate { UpdateFooterLayout(); };
            _toolTip.SetToolTip(_pnlStatusBar, "Ctrl+Shift+V opens Dolly Paste. Ctrl+F searches. Ctrl+P pins. Ctrl+Space previews.");

            _pnlMain.Controls.Add(_pnlContent);
            _pnlMain.Controls.Add(_pnlTypeFilters);
            _pnlMain.Controls.Add(_pnlSearchFilter);
            _pnlMain.Controls.Add(_pnlHeader);
            _pnlMain.Controls.SetChildIndex(_pnlContent, 0);
            _pnlMain.Controls.SetChildIndex(_pnlDetailToolbar, 1);
            _pnlMain.Controls.SetChildIndex(_pnlTypeFilters, 2);
            _pnlMain.Controls.SetChildIndex(_pnlSearchFilter, 3);
            _pnlMain.Controls.SetChildIndex(_pnlHeader, 4);
            if (_isDemoMode || _isPreviewMode)
            {
                _pnlDemoBanner = new Panel { Dock = DockStyle.Top, Height = 16, BackColor = Brand.WarningBannerBg };
                _pnlDemoBanner.Controls.Add(new Label { Dock = DockStyle.Fill,
                    Text = "Preview mode — recording disabled", Font = _fontRegular80,
                    ForeColor = Brand.WarningBannerFg, TextAlign = ContentAlignment.MiddleCenter });
                _pnlMain.Controls.Add(_pnlDemoBanner);
                // Keep the title bar at the top, including in private preview mode.
                _pnlMain.Controls.SetChildIndex(_pnlDemoBanner, 2);
            }
            Controls.Add(_pnlMain);
            Controls.Add(_pnlStatusBar);
            UpdateNavStyles();
            UpdateFilterPillStyles();
            UpdatePauseResumeState();
            ResumeLayout(true);
            UpdateHeaderLayout();
            UpdateSearchFilterLayout();
            UpdateFooterLayout();
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, string lParam);

        private Button CreateActionButton(string text, int width, EventHandler action)
        {
            Button button = new BrandButton { Text = text, Width = width, Height = 28,
                Font = _fontRegular85, FlatStyle = FlatStyle.Flat, BackColor = Brand.WarmWoolCanvas,
                ForeColor = Brand.TextPrimary, AccessibleName = text };
            Brand.StyleButton(button, false);
            button.Click += action;
            return button;
        }

        private void UpdateHeaderLayout()
        {
            if (_btnClose == null) return;
            _btnClose.SetBounds(_pnlHeader.ClientSize.Width - 40, 4, 32, 32);
        }

        private void UpdateSearchFilterLayout()
        {
            if (_btnNavAll == null || _btnNavPinned == null) return;
            _btnNavPinned.SetBounds(_pnlSearchFilter.ClientSize.Width - 69, 4, 60, 30);
            _btnNavAll.SetBounds(_btnNavPinned.Left - 44, 4, 42, 30);
            _pnlSearchBox.SetBounds(9, 1, Math.Max(1, _btnNavAll.Left - 19), 36);
            _txtSearch.SetBounds(34, 9, Math.Max(1, _pnlSearchBox.Width - 45), 20);
            _pnlSearchBox.Invalidate();
        }

        private void UpdateFooterLayout()
        {
            if (_btnSettings == null || _lblToast == null) return;
            _btnSettings.SetBounds(_pnlStatusBar.ClientSize.Width - 39, 0, 30, 28);
            _btnPauseResume.SetBounds(_btnSettings.Left - 84, 0, 80, 28);
            _lblStatusRight.SetBounds(9, 0, 56, 28);
            _lblStatusLeft.SetBounds(72, 0, Math.Max(1, _btnPauseResume.Left - 78), 28);
            _lblToast.SetBounds(9, 0, Math.Max(1, _btnPauseResume.Left - 15), 28);
        }

        private static void ConfigureIconButton(Button button, string icon, bool hideText)
        {
            BrandButton brand = button as BrandButton;
            if (brand != null) { brand.IconName = icon; brand.HideText = hideText; }
            button.FlatAppearance.BorderSize = 0;
            button.ForeColor = Brand.TextSecondary;
            button.BackColor = Brand.Surface;
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll", EntryPoint = "SendMessage")]
        private static extern IntPtr SendWindowMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

        private void OnTitleBarMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendWindowMessage(Handle, 0x00A1, new IntPtr(2), IntPtr.Zero);
        }

        private void TogglePreview()
        {
            _previewExpanded = !_previewExpanded;
            _pnlDetail.Visible = _previewExpanded;
            _btnPreview.Text = _previewExpanded ? "Hide" : "Preview";
            _btnPreview.BackColor = _previewExpanded ? Brand.AccentSoft : Brand.Surface;
            _btnPreview.ForeColor = _previewExpanded ? Brand.HeaderBlue : Brand.TextSecondary;
            _btnPreview.AccessibleDescription = _previewExpanded ? "Preview expanded" : "Preview collapsed";
            UpdatePreviewHeight();
        }

        private void UpdatePreviewHeight()
        {
            bool hasImage = _picImagePreview != null && _picImagePreview.Image != null;
            int minimumListHeight = 100;
            if (_pnlDetailMeta != null) _pnlDetailMeta.Height = hasImage ? 24 : 28;
            if (_pnlTextContainer != null)
                _pnlTextContainer.Padding = hasImage ? new Padding(14, 0, 14, 4) : new Padding(14, 3, 14, 6);
            if (_pnlDetail != null && _pnlContent != null)
                _pnlDetail.Height = Math.Max(0, Math.Min(168, _pnlContent.ClientSize.Height - minimumListHeight));
        }

        private void UpdateEmptyStateLayout()
        {
            if (_pnlEmptyState == null || _lblEmptyTitle == null || _lblEmptySubtitle == null) return;
            int w = Math.Max(100, _pnlEmptyState.ClientSize.Width - 32);
            _lblEmptyTitle.Size = new Size(w, 28);
            _lblEmptySubtitle.Size = new Size(w, 52);
            int startY = Math.Max(20, (_pnlEmptyState.ClientSize.Height - 80) / 3);
            _lblEmptyTitle.Location = new Point(16, startY);
            _lblEmptySubtitle.Location = new Point(16, startY + 32);
        }



        private void InitializeDetailPane()
        {
            _pnlDetail = new Panel();
            _pnlDetail.Dock = DockStyle.Right;
            _pnlDetail.Width = 380;
            _pnlDetail.BackColor = Color.White;
            _pnlDetail.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen p = new Pen(Brand.Border, 1))
                {
                    e.Graphics.DrawLine(p, 0, 0, _pnlDetail.Width, 0);
                }
            };

            // Empty detail state
            _pnlDetailEmpty = new Panel();
            _pnlDetailEmpty.Dock = DockStyle.Fill;
            _pnlDetailEmpty.BackColor = Color.White;

            Label lblDetailEmpty = new Label();
            lblDetailEmpty.Text = "Select a clip to preview its content";
            lblDetailEmpty.Font = _fontPreviewRegular;
            lblDetailEmpty.ForeColor = Brand.TextSecondary;
            lblDetailEmpty.TextAlign = ContentAlignment.MiddleCenter;
            lblDetailEmpty.Dock = DockStyle.Fill;
            _pnlDetailEmpty.Controls.Add(lblDetailEmpty);

            _pnlDetail.Controls.Add(_pnlDetailEmpty);

            // Populated detail content
            _pnlDetailContent = new Panel();
            _pnlDetailContent.Dock = DockStyle.Fill;
            _pnlDetailContent.BackColor = Color.White;
            _pnlDetailContent.Visible = false;

            _pnlDetailToolbar = new Panel();
            _pnlDetailToolbar.Dock = DockStyle.Top;
            _pnlDetailToolbar.Height = 52;
            _pnlDetailToolbar.BackColor = Color.White;
            _pnlDetailToolbar.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen p = new Pen(Brand.BorderLight, 1))
                {
                    e.Graphics.DrawLine(p, 0, 0, _pnlDetailToolbar.Width, 0);
                }
            };

            _btnCopy = new BrandButton();
            _btnCopy.Text = "Copy Text";
            _btnCopy.Font = _fontBold85;
            _btnCopy.BackColor = Brand.MossGreen;
            _btnCopy.ForeColor = Color.White;
            _btnCopy.FlatStyle = FlatStyle.Flat;
            _btnCopy.FlatAppearance.BorderSize = 0;
            _btnCopy.Size = new Size(88, 30);
            _btnCopy.Location = new Point(14, 11);
            _btnCopy.AccessibleName = "Copy selected clip to clipboard";
            _btnCopy.Click += delegate { CopySelectedClip(); };
            Brand.StyleButton(_btnCopy, true);
            _pnlDetailToolbar.Controls.Add(_btnCopy);

            _btnPin = new BrandButton();
            _btnPin.Text = "☆ Pin";
            _btnPin.Font = _fontRegular85;
            _btnPin.BackColor = Brand.WarmWoolCanvas;
            _btnPin.ForeColor = Brand.TextPrimary;
            _btnPin.FlatStyle = FlatStyle.Flat;
            _btnPin.FlatAppearance.BorderColor = Brand.Border;
            _btnPin.Size = new Size(80, 30);
            _btnPin.Location = new Point(110, 11);
            _btnPin.AccessibleName = "Toggle pin state for selected clip";
            _btnPin.Click += delegate { TogglePinSelectedClip(); };
            Brand.StyleButton(_btnPin, false);
            _pnlDetailToolbar.Controls.Add(_btnPin);

            _btnDelete = new BrandButton();
            _btnDelete.Text = "Delete";
            _btnDelete.Font = _fontRegular85;
            _btnDelete.BackColor = Brand.WarmWoolCanvas;
            _btnDelete.ForeColor = Brand.SensitiveFg;
            _btnDelete.FlatStyle = FlatStyle.Flat;
            _btnDelete.FlatAppearance.BorderColor = Color.FromArgb(240, 200, 200);
            _btnDelete.Size = new Size(68, 30);
            _btnDelete.Location = new Point(198, 11);
            _btnDelete.AccessibleName = "Delete selected clip";
            _btnDelete.Click += delegate { DeleteSelectedClip(); };
            Brand.StyleButton(_btnDelete, false);
            _pnlDetailToolbar.Controls.Add(_btnDelete);

            _pnlDetailMeta = new Panel();
            _pnlDetailMeta.Dock = DockStyle.Top;
            _pnlDetailMeta.Height = 28;
            _pnlDetailMeta.BackColor = Color.White;

            _lblDetailKind = new Label();
            _lblDetailKind.Location = new Point(14, 4);
            _lblDetailKind.Size = new Size(56, 20);
            _lblDetailKind.Font = _fontBold80;
            _lblDetailKind.TextAlign = ContentAlignment.MiddleCenter;
            _pnlDetailMeta.Controls.Add(_lblDetailKind);

            _lblCharCount = new Label();
            _lblCharCount.Dock = DockStyle.Right;
            _lblCharCount.Width = 110;
            _lblCharCount.TextAlign = ContentAlignment.MiddleRight;
            _lblCharCount.Font = _fontRegular80;
            _lblCharCount.ForeColor = Brand.TextMuted;
            _pnlDetailMeta.Controls.Add(_lblCharCount);

            _lblDetailTime = new Label();
            _lblDetailTime.Location = new Point(78, 4);
            _lblDetailTime.Size = new Size(160, 20);
            _lblDetailTime.Font = _fontRegular85;
            _lblDetailTime.ForeColor = Brand.TextSecondary;
            _lblDetailTime.AutoEllipsis = true;
            _pnlDetailMeta.Controls.Add(_lblDetailTime);

            _pnlDetailMeta.Resize += delegate
            {
                UpdateDetailMetaLayout();
            };
            UpdateDetailMetaLayout();

            _pnlTextContainer = new Panel();
            _pnlTextContainer.Dock = DockStyle.Fill;
            _pnlTextContainer.Padding = new Padding(14, 3, 14, 6);
            _pnlTextContainer.BackColor = Color.White;

            _txtPreview = new PrivacyTextBox(true, delegate { CopySelectedClip(); });
            _txtPreview.Dock = DockStyle.Fill;
            _txtPreview.Multiline = true;
            _txtPreview.ReadOnly = true;
            _txtPreview.WordWrap = true;
            _txtPreview.ScrollBars = ScrollBars.Vertical;
            _txtPreview.BackColor = Brand.WarmWoolCanvas;
            _txtPreview.ForeColor = Brand.TextPrimary;
            _txtPreview.BorderStyle = BorderStyle.None;
            _txtPreview.Font = _fontPreviewRegular;
            _txtPreview.AccessibleName = "Clip content preview";
            _pnlTextContainer.Controls.Add(_txtPreview);

            _pnlImagePreview = new Panel { Dock = DockStyle.Left, Width = 64,
                Padding = new Padding(0, 0, 10, 0), Visible = false };
            _picImagePreview = new PictureBox { Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom, BackColor = Brand.WarmWoolCanvas,
                AccessibleName = "Copied image preview", TabStop = false };
            _pnlImagePreview.Controls.Add(_picImagePreview);
            _pnlTextContainer.Controls.Add(_pnlImagePreview);

            _pnlOcrPreview = new Panel { Dock = DockStyle.Right, Width = 120,
                Padding = new Padding(8, 0, 0, 0), Visible = false };
            _txtOcrPreview = new PrivacyTextBox(true, delegate { CopySelectedClip(); });
            _txtOcrPreview.Dock = DockStyle.Fill;
            _txtOcrPreview.Multiline = true;
            _txtOcrPreview.ReadOnly = true;
            _txtOcrPreview.WordWrap = true;
            _txtOcrPreview.ScrollBars = ScrollBars.Vertical;
            _txtOcrPreview.BackColor = Brand.WarmWoolCanvas;
            _txtOcrPreview.ForeColor = Brand.TextPrimary;
            _txtOcrPreview.BorderStyle = BorderStyle.None;
            _txtOcrPreview.Font = _fontPreviewRegular;
            _txtOcrPreview.AccessibleName = "Extracted image text";
            _pnlOcrPreview.Controls.Add(_txtOcrPreview);
            _pnlTextContainer.Controls.Add(_pnlOcrPreview);
            _pnlTextContainer.Controls.SetChildIndex(_txtPreview, 0);
            _pnlTextContainer.Controls.SetChildIndex(_pnlOcrPreview, 1);
            _pnlTextContainer.Controls.SetChildIndex(_pnlImagePreview, 2);
            _pnlTextContainer.Resize += delegate { UpdateImageValueLayout(); };

            // Image controls replace the metadata row, preserving room for the preview
            // and one full history item even at the minimum popup size.
            _pnlImageActions = new Panel { Dock = DockStyle.Top, Height = 28, Visible = false,
                BackColor = Color.White, AccessibleName = "Image preview and text actions" };
            _btnCopyImageBase64 = CreateActionButton("Copy Base64", 94, delegate
            {
                if (SelectImageCopyTarget(ImageCopyTarget.Base64)) CopySelectedClip();
            });
            _btnCopyImageBase64.AccessibleName = "Copy image PNG base64 to clipboard";
            _btnCopyImageText = CreateActionButton("Copy text", 86, delegate { CopySelectedImageText(); });
            _btnCopyImageText.AccessibleName = "Copy extracted image text to clipboard";
            _btnCopyImageText.Enabled = false;
            _pnlImageActions.Controls.AddRange(new Control[] { _btnCopyImageBase64, _btnCopyImageText });
            _toolTip.SetToolTip(_btnCopyImageBase64, "Copy the full PNG base64 value and close (Enter or Ctrl+C)");
            _toolTip.SetToolTip(_btnCopyImageText, "Copy extracted text and close");
            UpdateImageValueLayout();

            // Detail Content Order: Fill (_pnlTextContainer at 0), Top (_pnlDetailMeta at 1), Top (_pnlDetailToolbar at 2)
            _pnlDetailContent.Controls.Add(_pnlTextContainer);
            _pnlDetailContent.Controls.Add(_pnlDetailMeta);
            _pnlDetailContent.Controls.Add(_pnlImageActions);
            _pnlDetailContent.Controls.Add(_pnlDetailToolbar);
            _pnlDetailContent.Controls.SetChildIndex(_pnlTextContainer, 0);
            _pnlDetailContent.Controls.SetChildIndex(_pnlDetailMeta, 1);
            _pnlDetailContent.Controls.SetChildIndex(_pnlImageActions, 2);
            _pnlDetailContent.Controls.SetChildIndex(_pnlDetailToolbar, 3);

            _pnlDetail.Controls.Add(_pnlDetailContent);
        }

        private void UpdateDetailMetaLayout()
        {
            if (_pnlDetailMeta == null || _lblDetailKind == null || _lblDetailTime == null || _lblCharCount == null) return;
            int kindWidth = string.Equals(_lblDetailKind.Text, "PROTECTED", StringComparison.OrdinalIgnoreCase) ? 86 : 56;
            _lblDetailKind.Location = new Point(14, 4);
            _lblDetailKind.Size = new Size(kindWidth, 20);

            int timeX = _lblDetailKind.Right + 8;
            _lblDetailTime.Location = new Point(timeX, 4);

            int countLeft = _lblCharCount.Left;
            if (countLeft <= 0)
            {
                countLeft = _pnlDetailMeta.ClientSize.Width - _lblCharCount.Width;
            }
            int timeWidth = Math.Max(20, countLeft - timeX - 8);
            _lblDetailTime.Size = new Size(timeWidth, 20);
        }

        private void UpdateImageValueLayout()
        {
            if (_pnlOcrPreview == null || _btnCopyImageBase64 == null || _btnCopyImageText == null) return;
            int valueLeft = _pnlTextContainer.Padding.Left + _pnlImagePreview.Width;
            int availableWidth = Math.Max(2, _pnlTextContainer.ClientSize.Width - valueLeft - _pnlTextContainer.Padding.Right);
            int ocrWidth = availableWidth / 2;
            int base64Width = availableWidth - ocrWidth;
            _pnlOcrPreview.Width = ocrWidth;
            _btnCopyImageBase64.SetBounds(valueLeft, 1, Math.Max(1, Math.Min(94, base64Width - 4)), 25);
            _btnCopyImageText.SetBounds(valueLeft + base64Width + 8, 1, Math.Max(1, Math.Min(86, ocrWidth - 8)), 25);
        }

        private Button CreateFilterPill(string text, string kind, int width, ref int currentX)
        {
            Button btn = new BrandButton();
            btn.Text = text;
            btn.Tag = kind;
            btn.Font = _fontRegular85;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.Size = new Size(width, 26);
            btn.Location = new Point(currentX, 7);
            btn.AccessibleName = string.Format("Filter by {0}", text);
            btn.Click += delegate
            {
                _currentKindFilter = kind;
                UpdateFilterPillStyles();
                ApplyFilters();
            };
            _pnlTypeFilters.Controls.Add(btn);
            currentX += width + 4;
            return btn;
        }

        private void InitializeTray()
        {
            _trayMenu = new ContextMenu();

            MenuItem itemShow = new MenuItem("Show Dolly Paste", delegate { ShowFromTray(); });
            itemShow.DefaultItem = true;
            _trayMenu.MenuItems.Add(itemShow);

            _trayMenu.MenuItems.Add("-");

            _trayItemPause = new MenuItem("Pause recording", delegate { OnPauseResumeClicked(null, null); });
            _trayMenu.MenuItems.Add(_trayItemPause);

            MenuItem itemSettings = new MenuItem("Settings...", delegate { OpenSettings(); });
            _trayMenu.MenuItems.Add(itemSettings);

            _trayMenu.MenuItems.Add("-");

            MenuItem itemQuit = new MenuItem("Quit", delegate
            {
                _isQuitting = true;
                Application.Exit();
            });
            _trayMenu.MenuItems.Add(itemQuit);

            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = _formIcon;
            _notifyIcon.Text = "Dolly Paste";
            _notifyIcon.ContextMenu = _trayMenu;
            _notifyIcon.Visible = true;
            _notifyIcon.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) ShowFromTray();
            };
        }

        private void InitializeTimers()
        {
            _pruneTimer = new Timer();
            _pruneTimer.Interval = 30000;
            _pruneTimer.Tick += OnPruneTimerTick;
            _pruneTimer.Start();

            _statusTimer = new Timer();
            _statusTimer.Interval = 15000;
            _statusTimer.Tick += delegate
            {
                if (_listBoxClips != null && !_listBoxClips.IsDisposed)
                {
                    _listBoxClips.Invalidate();
                }
                UpdateStatusBar();
            };
            _statusTimer.Start();

            _toastTimer = new Timer();
            _toastTimer.Interval = 2000;
            _toastTimer.Tick += delegate
            {
                _toastTimer.Stop();
                _lblToast.Visible = false;
                if (_lblStatusLeft != null)
                {
                    _lblStatusLeft.Visible = true;
                }
                if (_lblStatusRight != null) _lblStatusRight.Visible = true;
            };
        }

        private void OnHistoryChanged(object sender, EventArgs e)
        {
            if (_isDisposed) return;

            if (this.IsHandleCreated && this.InvokeRequired)
            {
                try
                {
                    this.BeginInvoke(new Action(OnHistoryChangedSafe));
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
                return;
            }
            OnHistoryChangedSafe();
        }

        private void OnHistoryChangedSafe()
        {
            if (_isDisposed) return;
            RefreshClipsList();
        }

        private void OnPruneTimerTick(object sender, EventArgs e)
        {
            if (_isDisposed || _history == null) return;
            _history.Prune();
            RefreshClipsList();
        }

        private void RefreshClipsList()
        {
            if (_isDisposed || _isRefreshing || _history == null) return;
            try
            {
                _isRefreshing = true;
                _history.Prune();
                _allClips = _history.Snapshot();
                RemoveStaleImageThumbnails();

                ApplyFilters();
                UpdateNavStyles();
                UpdateStatusBar();
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        private void ApplyFilters()
        {
            Guid? preservedId = _selectedClipId;
            string query = (_txtSearch.Text ?? "").Trim();

            List<ClipEntry> filtered = new List<ClipEntry>();
            for (int i = 0; i < _allClips.Count; i++)
            {
                ClipEntry entry = _allClips[i];

                if (_currentNav == NavMode.Pinned && !entry.IsPinned)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(_currentKindFilter))
                {
                    if (!string.Equals(entry.Kind, _currentKindFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                if (!string.IsNullOrEmpty(query))
                {
                    if (entry.IsSensitive)
                    {
                        if ("protected content removed".IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        string text = entry.Text ?? "";
                        string imageText = entry.OcrText ?? "";
                        if (text.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                            imageText.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }
                    }
                }

                filtered.Add(entry);
            }

            _displayedClips = filtered;

            _changingListItems = true;
            _listBoxClips.BeginUpdate();
            try
            {
            _listBoxClips.Items.Clear();
            for (int i = 0; i < _displayedClips.Count; i++)
            {
                _listBoxClips.Items.Add(_displayedClips[i]);
            }

            int targetIndex = -1;
            if (preservedId.HasValue)
            {
                for (int i = 0; i < _displayedClips.Count; i++)
                {
                    if (_displayedClips[i].Id == preservedId.Value)
                    {
                        targetIndex = i;
                        break;
                    }
                }
            }

            if (targetIndex >= 0)
            {
                _listBoxClips.SelectedIndex = targetIndex;
            }
            else if (_displayedClips.Count > 0)
            {
                _listBoxClips.SelectedIndex = 0;
            }
            else
            {
                _listBoxClips.SelectedIndex = -1;
            }

            }
            finally
            {
                _listBoxClips.EndUpdate();
                _changingListItems = false;
            }

            if (_displayedClips.Count == 0)
            {
                _pnlEmptyState.Visible = true;
                _pnlEmptyState.BringToFront();

                if (_clipboard != null && _clipboard.Paused)
                {
                    _lblEmptyTitle.Text = "Capture is paused";
                    _lblEmptySubtitle.Text = "Clipboard recording is currently paused.\nClick Resume below to start capturing clips.";
                }
                else if (!string.IsNullOrEmpty(query))
                {
                    _lblEmptyTitle.Text = "No matching clips";
                    _lblEmptySubtitle.Text = string.Format("No clips found matching \"{0}\".\nTry clearing your search query or filter tags.", query);
                }
                else if (_currentNav == NavMode.Pinned)
                {
                    _lblEmptyTitle.Text = "No pinned clips";
                    _lblEmptySubtitle.Text = "Choose All, select a clip, then click Pin.\nIf a type filter is active, try Any.";
                }
                else if (!string.IsNullOrEmpty(_currentKindFilter))
                {
                    _lblEmptyTitle.Text = "No clips of this type";
                    _lblEmptySubtitle.Text = "Try another type or choose Any\nto see all your clips.";
                }
                else
                {
                    _lblEmptyTitle.Text = "Clipboard history is empty";
                    _lblEmptySubtitle.Text = "Copy text or an image to save it here.\nClips are stored 100% locally on this device.";
                }
            }
            else
            {
                _pnlEmptyState.Visible = false;
            }

            UpdateDetailPane();
        }

        private void UpdateDetailPane()
        {
            if (_listBoxClips.SelectedIndex < 0 || _listBoxClips.SelectedIndex >= _displayedClips.Count)
            {
                _selectedClipId = null;
                _pnlDetailContent.Visible = false;
                _pnlDetailEmpty.Visible = true;
                _txtPreview.Text = "";
                ClearImagePreview();
                _btnCopy.Enabled = false;
                _btnPin.Enabled = false;
                _btnDelete.Enabled = false;
                _btnPreview.Enabled = false;
                _lblFormatHint.Visible = false;
                UpdateStatusBar();
                return;
            }

            ClipEntry clip = _displayedClips[_listBoxClips.SelectedIndex];
            Guid expectedId = clip.Id;
            if (_selectedClipId != expectedId) _imageCopyTarget = ImageCopyTarget.Image;
            _selectedClipId = expectedId;
            if (_imageCopyTarget == ImageCopyTarget.OcrText && !HasOcrText(clip))
                _imageCopyTarget = ImageCopyTarget.Image;

            if (_history != null)
            {
                _history.Prune();
                if (_isDisposed || _listBoxClips.SelectedIndex < 0 || _listBoxClips.SelectedIndex >= _displayedClips.Count)
                {
                    return;
                }
                if (_displayedClips[_listBoxClips.SelectedIndex].Id != expectedId)
                {
                    // Selection changed reentrantly during prune/refresh
                    return;
                }
                clip = _displayedClips[_listBoxClips.SelectedIndex];
            }

            _pnlDetailEmpty.Visible = false;
            _pnlDetailContent.Visible = true;

            _btnPin.Text = clip.IsPinned ? "★ Pinned" : "☆ Pin";
            _btnPin.ForeColor = clip.IsPinned ? Brand.HeaderBlue : Brand.TextSecondary;
            _btnPin.BackColor = clip.IsPinned ? Brand.AccentSoft : Brand.Surface;
            _btnPin.AccessibleName = clip.IsPinned ? "Unpin selected clip" : "Pin selected clip";
            _btnPin.Enabled = true;
            _btnDelete.Enabled = true;
            _btnPreview.Enabled = true;

            DateTime localTime = clip.CapturedUtc.ToLocalTime();
            _lblDetailTime.Text = localTime.ToString("MMM d, yyyy h:mm tt");

            if (clip.IsSensitive)
            {
                ClearImagePreview();
                _lblDetailKind.Text = "PROTECTED";
                _lblDetailKind.BackColor = Brand.SensitiveBg;
                _lblDetailKind.ForeColor = Brand.SensitiveFg;

                // Sensitive entries: payload never revealed or copied
                _txtPreview.Text = "Protected content removed\r\n\r\n" +
                                   "Saved text was removed and this entry cannot be copied.\r\n\r\n" +
                                   "This text was removed by an earlier version. Copy it again from its original source.";
                _txtPreview.Font = _fontPreviewItalic;
                _txtPreview.ForeColor = Brand.SensitiveFg;

                _btnCopy.Enabled = false;
                _lblCharCount.Text = "0 characters";
                UpdateDetailMetaLayout();
            }
            else
            {
                string kind = clip.Kind ?? "TEXT";
                _lblDetailKind.Text = kind;
                _lblDetailKind.BackColor = Brand.GetKindBadgeBg(kind);
                _lblDetailKind.ForeColor = Brand.GetKindBadgeFg(kind);

                // Fetch fresh text directly from HistoryStore to avoid stale payloads
                string fullText = _history != null ? _history.GetText(expectedId) : null;

                if (_isDisposed || _listBoxClips.SelectedIndex < 0 || _listBoxClips.SelectedIndex >= _displayedClips.Count)
                {
                    return;
                }
                if (_displayedClips[_listBoxClips.SelectedIndex].Id != expectedId)
                {
                    // Selection changed reentrantly during GetText
                    return;
                }

                if (fullText != null)
                {
                    UpdateImagePreview(clip, fullText);
                    bool isImage = string.Equals(kind, "IMAGE", StringComparison.OrdinalIgnoreCase);
                    // Native multiline edit controls expect CRLF; keep stored/copied text unchanged.
                    _txtPreview.Text = fullText.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
                    if (string.Equals(kind, "CODE", StringComparison.OrdinalIgnoreCase) ||
                        isImage)
                    {
                        _txtPreview.Font = _fontPreviewCode;
                    }
                    else
                    {
                        _txtPreview.Font = _fontPreviewRegular;
                    }
                    _txtPreview.ForeColor = Brand.TextPrimary;

                    _btnCopy.Enabled = true;

                    int chars = fullText.Length;
                    int lines = fullText.Split('\n').Length;
                    _lblCharCount.Text = string.Format("{0:N0} chars · {1} {2}", chars, lines, lines == 1 ? "line" : "lines");
                }
                else
                {
                    // If GetText returned null, the clip expired or was removed.
                    // Route through guarded refresh so surviving clip or clean empty state is selected and displayed.
                    RefreshClipsList();
                    return;
                }
                UpdateDetailMetaLayout();
            }
            UpdateCopyTargetAppearance();
        }

        private void UpdateImagePreview(ClipEntry clip, string base64)
        {
            if (!string.Equals(clip.Kind, "IMAGE", StringComparison.OrdinalIgnoreCase))
            {
                ClearImagePreview();
                return;
            }
            if (_previewImageId != clip.Id)
            {
                ClearImagePreview();
                _picImagePreview.Image = ImagePayload.CreateThumbnail(base64, new Size(240, 240));
                _previewImageId = clip.Id;
            }
            _pnlImagePreview.Visible = _picImagePreview.Image != null;
            // Native edit controls take seconds to wrap a large, unbroken base64
            // value in a narrow column. Keep it intact and scroll horizontally.
            _txtPreview.WordWrap = false;
            _txtPreview.ScrollBars = ScrollBars.Both;
            _pnlDetailMeta.Visible = false;
            _pnlImageActions.Visible = true;
            _pnlOcrPreview.Visible = true;
            _txtOcrPreview.Text = GetImageTextPreview(clip).Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
            _txtOcrPreview.ForeColor = string.IsNullOrWhiteSpace(clip.OcrText) ? Brand.TextSecondary : Brand.TextPrimary;
            _btnCopyImageBase64.Enabled = true;
            _btnCopyImageText.Enabled = clip.OcrStatus == OcrStates.Complete && !string.IsNullOrWhiteSpace(clip.OcrText);
            _txtPreview.AccessibleName = "Image PNG base64 value";
            UpdatePreviewHeight();
            UpdateImageValueLayout();
        }

        private void ClearImagePreview()
        {
            Image previous = _picImagePreview.Image;
            _picImagePreview.Image = null;
            if (previous != null) previous.Dispose();
            _previewImageId = null;
            _pnlImagePreview.Visible = false;
            // Clear the previous image value before restoring normal text wrapping.
            if (!_txtPreview.WordWrap) _txtPreview.Clear();
            _txtPreview.WordWrap = true;
            _txtPreview.ScrollBars = ScrollBars.Vertical;
            _pnlImageActions.Visible = false;
            _pnlOcrPreview.Visible = false;
            _txtOcrPreview.Text = "";
            _pnlDetailMeta.Visible = true;
            _btnCopyImageBase64.Enabled = false;
            _btnCopyImageText.Enabled = false;
            _txtPreview.AccessibleName = "Clip content preview";
            _btnCopy.Text = "Copy text";
            _btnCopy.AccessibleName = "Copy selected clip to clipboard";
            _toolTip.SetToolTip(_btnCopy, "Copy selected clip and close (Enter or Ctrl+C)");
            UpdatePreviewHeight();
        }

        private static string GetImageTextPreview(ClipEntry clip)
        {
            if (clip.OcrStatus == OcrStates.Complete && !string.IsNullOrWhiteSpace(clip.OcrText))
                return clip.OcrText;
            if (clip.OcrStatus == OcrStates.NoText)
                return "No text found in this image.";
            if (clip.OcrStatus == OcrStates.Unavailable)
                return "Text recognition is unavailable.\r\n\r\nCheck the OCR language capability in Windows language settings. Availability in the portable app depends on Windows; image and Base64 copying remain available.";
            if (clip.OcrStatus == OcrStates.Failed)
                return "Text recognition could not finish.\r\n\r\nRestart Dolly Paste to retry.";
            return "Reading text from this image...";
        }

        private Bitmap GetImageThumbnail(ClipEntry clip)
        {
            Bitmap thumbnail;
            if (!_imageThumbnails.TryGetValue(clip.Id, out thumbnail))
            {
                if (_imageThumbnails.Count >= MaxCachedImageThumbnails) ClearImageThumbnails();
                thumbnail = ImagePayload.CreateThumbnail(clip.Text, new Size(38, 38));
                _imageThumbnails.Add(clip.Id, thumbnail);
            }
            return thumbnail;
        }

        private void ClearImageThumbnails()
        {
            foreach (Bitmap thumbnail in _imageThumbnails.Values)
            {
                if (thumbnail != null) thumbnail.Dispose();
            }
            _imageThumbnails.Clear();
        }

        private void RemoveStaleImageThumbnails()
        {
            HashSet<Guid> retained = new HashSet<Guid>();
            foreach (ClipEntry entry in _allClips)
                if (entry.Kind == "IMAGE" && !entry.IsSensitive) retained.Add(entry.Id);
            foreach (Guid id in new List<Guid>(_imageThumbnails.Keys))
                if (!retained.Contains(id))
                {
                    if (_imageThumbnails[id] != null) _imageThumbnails[id].Dispose();
                    _imageThumbnails.Remove(id);
                }
        }

        private static bool HasOcrText(ClipEntry entry)
        {
            return entry != null && !entry.IsSensitive && entry.Kind == "IMAGE" &&
                entry.OcrStatus == OcrStates.Complete && !string.IsNullOrWhiteSpace(entry.OcrText);
        }

        private ClipEntry SelectedEntry
        {
            get
            {
                int index = _listBoxClips.SelectedIndex;
                return index >= 0 && index < _displayedClips.Count ? _displayedClips[index] : null;
            }
        }

        internal ImageCopyTarget SelectedImageCopyTarget { get { return _imageCopyTarget; } }

        internal bool SelectImageCopyTarget(ImageCopyTarget target)
        {
            ClipEntry entry = SelectedEntry;
            if (entry == null || entry.IsSensitive || entry.Kind != "IMAGE" ||
                (target == ImageCopyTarget.OcrText && !HasOcrText(entry))) return false;
            if (target != ImageCopyTarget.Image && target != ImageCopyTarget.OcrText && target != ImageCopyTarget.Base64)
                return false;
            _imageCopyTarget = target;
            UpdateCopyTargetAppearance();
            _listBoxClips.Invalidate();
            return true;
        }

        private void UpdateCopyTargetAppearance()
        {
            ClipEntry entry = SelectedEntry;
            bool image = entry != null && !entry.IsSensitive && entry.Kind == "IMAGE";
            string action = image ? (_imageCopyTarget == ImageCopyTarget.Image ? "Copy image" :
                _imageCopyTarget == ImageCopyTarget.OcrText ? "Copy text" : "Copy Base64") : "Copy text";
            _btnCopy.Text = action;
            _btnCopy.AccessibleName = action + " to clipboard";
            _listBoxClips.AccessibleDescription = image ? action + " selected. Left and Right change the format." : "Enter copies the selected text clip.";
            _lblFormatHint.Visible = image;
            _toolTip.SetToolTip(_btnCopy, action + " and close (Enter or Ctrl+C)");
            UpdateStatusBar();
        }

        private bool MoveImageCopyTarget(int direction)
        {
            ClipEntry entry = SelectedEntry;
            if (entry == null || entry.IsSensitive || entry.Kind != "IMAGE") return false;
            ImageCopyTarget[] targets = HasOcrText(entry) ?
                new[] { ImageCopyTarget.Image, ImageCopyTarget.OcrText, ImageCopyTarget.Base64 } :
                new[] { ImageCopyTarget.Image, ImageCopyTarget.Base64 };
            int index = Array.IndexOf(targets, _imageCopyTarget);
            return SelectImageCopyTarget(targets[Math.Max(0, Math.Min(targets.Length - 1, index + direction))]);
        }

        private void CopySelectedClip()
        {
            CopySelectedPayload();
        }

        private void CopySelectedImageText()
        {
            if (SelectImageCopyTarget(ImageCopyTarget.OcrText)) CopySelectedPayload();
        }

        private void CopySelectedPayload()
        {
            if (!_selectedClipId.HasValue) return;

            Guid id = _selectedClipId.Value;
            _history.Prune();

            ClipEntry entry = null;
            if (_allClips != null)
            {
                for (int i = 0; i < _allClips.Count; i++)
                {
                    if (_allClips[i].Id == id)
                    {
                        entry = _allClips[i];
                        break;
                    }
                }
            }

            if (entry == null || entry.IsSensitive)
            {
                return;
            }

            bool isImage = entry.Kind == "IMAGE";
            ImageCopyTarget target = _imageCopyTarget;
            bool imageText = isImage && target == ImageCopyTarget.OcrText;
            string text = imageText ? _history.GetOcrText(id) : _history.GetText(id);
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (_isDemoMode || _isPreviewMode)
            {
                ShowToast(!isImage ? "Preview mode: copy simulated" : imageText ? "Preview mode: text copy simulated" :
                    target == ImageCopyTarget.Image ? "Preview mode: image copy simulated" : "Preview mode: Base64 copy simulated");
                return;
            }

            if (_clipboard != null)
            {
                Action<uint> trackCopy = delegate(uint sequence)
                {
                    if (imageText) _history.TrackRestoredOcr(id, sequence);
                    else _history.TrackRestored(id, sequence);
                };
                bool success = isImage && target == ImageCopyTarget.Image ?
                    _clipboard.CopyImage(text, trackCopy) : _clipboard.CopyText(text, trackCopy);

                if (success)
                {
                    this.Hide();
                }
                else
                {
                    ShowToast("Clipboard busy. Try again.");
                }
            }
        }

        private void DeleteSelectedClip()
        {
            if (!_selectedClipId.HasValue) return;

            Guid id = _selectedClipId.Value;
            _history.Remove(id);
            RefreshClipsList();
        }

        private void TogglePinSelectedClip()
        {
            if (!_selectedClipId.HasValue) return;

            Guid id = _selectedClipId.Value;
            _history.TogglePin(id);
            RefreshClipsList();
        }

        private void ShowToast(string message)
        {
            _lblToast.Text = "✓ " + message;
            _lblToast.Visible = true;
            if (_lblStatusLeft != null)
            {
                _lblStatusLeft.Visible = false;
            }
            _lblStatusRight.Visible = false;
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        private void OnPauseResumeClicked(object sender, EventArgs e)
        {
            if (_isDemoMode || _isPreviewMode) return;

            if (_clipboard != null)
            {
                _clipboard.Paused = !_clipboard.Paused;
                UpdatePauseResumeState();
                ApplyFilters();
                UpdateStatusBar();
            }
        }

        private void UpdatePauseResumeState()
        {
            bool isPaused = _isDemoMode || _isPreviewMode || (_clipboard != null && _clipboard.Paused);

            if (isPaused)
            {
                _btnPauseResume.Text = "Resume";
                if (_trayItemPause != null) _trayItemPause.Text = "Resume recording";
            }
            else
            {
                _btnPauseResume.Text = "Pause";
                if (_trayItemPause != null) _trayItemPause.Text = "Pause recording";
            }

            if (_isDemoMode || _isPreviewMode)
            {
                _btnPauseResume.Enabled = false;
                _btnPauseResume.Text = "Paused";
            }
            ((BrandButton)_btnPauseResume).IconName = isPaused ? "play" : "pause";
            _btnPauseResume.BackColor = isPaused ? Brand.AccentSoft : Brand.Surface;
            _btnPauseResume.ForeColor = isPaused ? Brand.HeaderBlue : Brand.TextSecondary;
            _btnPauseResume.AccessibleDescription = isPaused ? "Clipboard recording paused" : "Clipboard recording active";
        }

        private void UpdateStatusBar()
        {
            if (_lblStatusLeft == null || _lblStatusRight == null) return;

            string statusLeft;
            if (_isDemoMode || _isPreviewMode)
            {
                statusLeft = "↑ ↓ select · Enter copy";
            }
            else if (_clipboard != null && !string.IsNullOrEmpty(_clipboard.Status) && _clipboard.Status != "Active")
            {
                statusLeft = _clipboard.Status;
            }
            else
            {
                statusLeft = "↑ ↓ select · Enter copy";
            }

            if (_history != null && !string.IsNullOrEmpty(_history.StorageWarning))
            {
                statusLeft = _history.StorageWarning + " | " + statusLeft;
            }

            _lblStatusLeft.Text = statusLeft;
            _toolTip.SetToolTip(_lblStatusLeft, statusLeft);

            int total = _displayedClips != null ? _displayedClips.Count : 0;
            string count = string.Format("{0} {1}", total, total == 1 ? "clip" : "clips");
            _lblStatusRight.Text = count;
            _lblClipCount.Text = count;
        }

        private void UpdateNavStyles()
        {
            if (_btnNavAll == null || _btnNavPinned == null) return;
            _btnNavAll.Text = "All";
            _btnNavPinned.Text = "Pinned";
            ApplyCollectionStyle(_btnNavAll, _currentNav == NavMode.All);
            ApplyCollectionStyle(_btnNavPinned, _currentNav == NavMode.Pinned);
        }

        private void ApplyCollectionStyle(Button button, bool active)
        {
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = active ? Brand.Surface : Brand.HeaderHover;
            button.ForeColor = active ? Brand.HeaderBlue : Color.White;
            button.FlatAppearance.MouseOverBackColor = active ? Brand.AccentSoft : Brand.HeaderHover;
            button.FlatAppearance.MouseDownBackColor = Brand.AccentSoft;
            button.AccessibleDescription = active ? "Selected collection" : "Show collection";
            button.Invalidate();
        }

        private void UpdateFilterPillStyles()
        {
            ApplyFilterPillStyle(_btnFilterAll, _currentKindFilter == null);
            ApplyFilterPillStyle(_btnFilterText, string.Equals(_currentKindFilter, "TEXT", StringComparison.OrdinalIgnoreCase));
            ApplyFilterPillStyle(_btnFilterLink, string.Equals(_currentKindFilter, "LINK", StringComparison.OrdinalIgnoreCase));
            ApplyFilterPillStyle(_btnFilterCode, string.Equals(_currentKindFilter, "CODE", StringComparison.OrdinalIgnoreCase));
            ApplyFilterPillStyle(_btnFilterImage, string.Equals(_currentKindFilter, "IMAGE", StringComparison.OrdinalIgnoreCase));
        }

        private void ApplyFilterPillStyle(Button btn, bool active)
        {
            if (btn == null) return;
            btn.FlatAppearance.BorderSize = 0;
            btn.BackColor = active ? Brand.AccentSoft : Brand.Surface;
            btn.ForeColor = active ? Brand.HeaderBlue : Brand.TextSecondary;
            btn.Font = active ? _fontBold85 : _fontRegular85;
            btn.FlatAppearance.MouseOverBackColor = Brand.CardHover;
            btn.FlatAppearance.MouseDownBackColor = Brand.AccentSoft;
            btn.AccessibleDescription = active ? "Selected content type" : "Filter content type";
            btn.Invalidate();
        }

        private void OpenSettings()
        {
            if (_settingsOpen) return;
            _settingsOpen = true;
            try
            {
                if (!Visible) ShowFromTray();
                using (SettingsDialog dlg = new SettingsDialog(_history.Settings, _history, _dataDirectory, _isDemoMode || _isPreviewMode))
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK) RefreshClipsList();
                }
            }
            finally
            {
                _settingsOpen = false;
                if (!_isDisposed && Visible) _txtSearch.Focus();
            }
        }

        internal static Rectangle GetPopupBounds(Rectangle workingArea, Point anchor, Size size)
        {
            int margin = Math.Min(8, Math.Max(0, Math.Min(workingArea.Width, workingArea.Height) / 4));
            int width = Math.Max(1, Math.Min(size.Width, workingArea.Width - margin * 2));
            int height = Math.Max(1, Math.Min(size.Height, workingArea.Height - margin * 2));
            int left = Math.Max(workingArea.Left + margin, Math.Min(anchor.X + 12, workingArea.Right - margin - width));
            int top = Math.Max(workingArea.Top + margin, Math.Min(anchor.Y + 12, workingArea.Bottom - margin - height));
            return new Rectangle(left, top, width, height);
        }

        public void ShowFromTray()
        {
            // Each invocation starts with the newest clips, ready for a fresh query.
            _currentNav = NavMode.All;
            _currentKindFilter = null;
            _selectedClipId = null;
            _txtSearch.Clear();
            if (_previewExpanded) TogglePreview();
            UpdateFilterPillStyles();
            RefreshClipsList();
            _toastTimer.Stop();
            _lblToast.Visible = false;
            _lblStatusLeft.Visible = true;
            _lblStatusRight.Visible = true;
            Rectangle workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size initialSize, minSize;
            ClampWindowSize(workArea, this.Size, new Size(420, 460), out initialSize, out minSize);
            this.MinimumSize = minSize;
            this.Bounds = GetPopupBounds(workArea, Cursor.Position, initialSize);
            this.WindowState = FormWindowState.Normal;
            if (!Visible) Show();
            Activate();
            BringToFront();
            _txtSearch.Focus();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _txtSearch.Focus();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!_isDemoMode && !_isPreviewMode && !_settingsOpen && !_isQuitting)
                Hide();
        }

        public void ToggleVisibility()
        {
            if (this.Visible && this.WindowState != FormWindowState.Minimized && this.ContainsFocus)
            {
                this.Hide();
            }
            else
            {
                ShowFromTray();
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.F))
            {
                _txtSearch.Focus();
                _txtSearch.SelectAll();
                return true;
            }
            if (keyData == Keys.Escape) { Hide(); return true; }
            if (keyData == (Keys.Control | Keys.Space)) { TogglePreview(); return true; }
            bool pickerFocused = _txtSearch.Focused || _listBoxClips.Focused;
            bool atSearchEdge = _txtSearch.SelectionLength == 0 &&
                ((keyData == Keys.Right && _txtSearch.SelectionStart == _txtSearch.TextLength) ||
                (keyData == Keys.Left && _txtSearch.SelectionStart == 0));
            if (pickerFocused && (keyData == Keys.Left || keyData == Keys.Right) &&
                (_listBoxClips.Focused || atSearchEdge) && MoveImageCopyTarget(keyData == Keys.Right ? 1 : -1))
                return true;
            // Native single-column list boxes otherwise treat Left/Right as item navigation.
            if (_listBoxClips.Focused && (keyData == Keys.Left || keyData == Keys.Right)) return true;
            if (pickerFocused && keyData == (Keys.Control | Keys.P))
            {
                TogglePinSelectedClip();
                return true;
            }
            if (_txtSearch.Focused && (keyData == Keys.Down || keyData == Keys.Up))
            {
                int count = _listBoxClips.Items.Count;
                if (count > 0)
                {
                    int next = _listBoxClips.SelectedIndex + (keyData == Keys.Down ? 1 : -1);
                    _listBoxClips.SelectedIndex = Math.Max(0, Math.Min(count - 1, next));
                }
                return true;
            }
            if ((keyData == Keys.Enter && pickerFocused) ||
                (keyData == (Keys.Control | Keys.C) &&
                    (_listBoxClips.Focused || (_txtSearch.Focused && _txtSearch.SelectionLength == 0))))
            {
                CopySelectedClip();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void OnListBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyData == Keys.Delete && !_txtSearch.Focused)
            {
                DeleteSelectedClip();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyData == Keys.Enter)
            {
                CopySelectedClip();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        internal Rectangle GetClipCardBounds(int index)
        {
            if (index < 0 || index >= _displayedClips.Count) return Rectangle.Empty;
            Rectangle row = _listBoxClips.GetItemRectangle(index);
            return new Rectangle(row.Left + 5, row.Top + 3, Math.Max(1, row.Width - 10), Math.Max(1, row.Height - 6));
        }

        internal Rectangle GetImageTargetBounds(int index, ImageCopyTarget target)
        {
            if (index < 0 || index >= _displayedClips.Count) return Rectangle.Empty;
            ClipEntry entry = _displayedClips[index];
            if (entry.IsSensitive || entry.Kind != "IMAGE" ||
                (target == ImageCopyTarget.OcrText && !HasOcrText(entry))) return Rectangle.Empty;
            Rectangle card = GetClipCardBounds(index);
            int left = card.Left + 63;
            if (target == ImageCopyTarget.Image) return new Rectangle(left, card.Top + 54, 65, 24);
            if (target == ImageCopyTarget.OcrText) return new Rectangle(left + 70, card.Top + 54, 57, 24);
            if (target == ImageCopyTarget.Base64) return new Rectangle(left + (HasOcrText(entry) ? 132 : 70), card.Top + 54, 76, 24);
            return Rectangle.Empty;
        }

        private void OnListBoxMeasureItem(object sender, MeasureItemEventArgs e)
        {
            ClipEntry entry = e.Index >= 0 && e.Index < _displayedClips.Count ? _displayedClips[e.Index] : null;
            e.ItemHeight = entry != null && !entry.IsSensitive && entry.Kind == "IMAGE" ? 90 : 42;
        }
        private void OnClipsMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            int index = _listBoxClips.IndexFromPoint(e.Location);
            if (index < 0 || index >= _displayedClips.Count) return;
            _listBoxClips.SelectedIndex = index;
            foreach (ImageCopyTarget target in new[] { ImageCopyTarget.Image, ImageCopyTarget.OcrText, ImageCopyTarget.Base64 })
                if (GetImageTargetBounds(index, target).Contains(e.Location))
                {
                    SelectImageCopyTarget(target);
                    break;
                }
            _listBoxClips.Focus();
        }

        private static void DrawRoundedCard(Graphics graphics, Rectangle bounds, Color fill, Color border, bool selected)
        {
            if (bounds.Width < 2 || bounds.Height < 2) return;
            RectangleF box = new RectangleF(bounds.X + 0.5f, bounds.Y + 0.5f, bounds.Width - 1, bounds.Height - 1);
            using (GraphicsPath path = Brand.CreateRoundedPath(box, 8))
            {
                SmoothingMode previous = graphics.SmoothingMode;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (Brush brush = new SolidBrush(fill)) graphics.FillPath(brush, path);
                using (Pen pen = new Pen(border, selected ? 1.6f : 1f)) graphics.DrawPath(pen, path);
                graphics.SmoothingMode = previous;
            }
        }

        private void OnListBoxDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _displayedClips.Count) return;
            ClipEntry entry = _displayedClips[e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Graphics graphics = e.Graphics;
            using (Brush canvas = new SolidBrush(Brand.Canvas)) graphics.FillRectangle(canvas, e.Bounds);
            Rectangle card = GetClipCardBounds(e.Index);
            DrawRoundedCard(graphics, card, Brand.Surface, selected ? Color.FromArgb(154, 193, 235) : Brand.BorderLight, selected);
            bool image = !entry.IsSensitive && entry.Kind == "IMAGE";
            int textLeft = card.Left + (image ? 63 : 49);
            int ageWidth = 61;
            Rectangle ageBounds = new Rectangle(card.Right - ageWidth - 9, card.Top + 8, ageWidth, 16);
            TextRenderer.DrawText(graphics, FormatRelativeTime(entry.CapturedUtc), _fontRegular80,
                ageBounds, Brand.TextMuted, TextFormatFlags.Right | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (entry.IsPinned)
                Brand.DrawIcon(graphics, "pin", new Rectangle(ageBounds.Left - 18, card.Top + 10, 12, 12), Brand.HeaderBlue);
            int titleWidth = Math.Max(1, ageBounds.Left - textLeft - (entry.IsPinned ? 22 : 6));

            if (image)
            {
                Rectangle thumbnailBox = new Rectangle(card.Left + 10, card.Top + 10, 42, 40);
                DrawRoundedCard(graphics, thumbnailBox, Brand.Canvas, Brand.BorderLight, false);
                Bitmap thumbnail = GetImageThumbnail(entry);
                if (thumbnail != null)
                    graphics.DrawImageUnscaled(thumbnail, thumbnailBox.Left + (thumbnailBox.Width - thumbnail.Width) / 2,
                        thumbnailBox.Top + (thumbnailBox.Height - thumbnail.Height) / 2);
                else Brand.DrawIcon(graphics, "image", new Rectangle(thumbnailBox.Left + 12, thumbnailBox.Top + 11, 18, 18), Brand.TextMuted);
                TextRenderer.DrawText(graphics, "Clipboard image", _fontBold90,
                    new Rectangle(textLeft, card.Top + 9, titleWidth, 18), Brand.TextPrimary,
                    TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                string snippet = HasOcrText(entry) ? entry.OcrText :
                    entry.OcrStatus == OcrStates.Pending ? "Reading text from image..." :
                    entry.OcrStatus == OcrStates.NoText ? "No text found in image" : "Text recognition unavailable";
                snippet = CompactSnippet(snippet, 200);
                TextRenderer.DrawText(graphics, snippet, _fontRegular85,
                    new Rectangle(textLeft, card.Top + 30, Math.Max(1, card.Right - textLeft - 12), 18), Brand.TextSecondary,
                    TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                foreach (ImageCopyTarget target in new[] { ImageCopyTarget.Image, ImageCopyTarget.OcrText, ImageCopyTarget.Base64 })
                {
                    Rectangle chip = GetImageTargetBounds(e.Index, target);
                    if (chip.IsEmpty) continue;
                    bool active = selected && target == _imageCopyTarget;
                    if (active) DrawRoundedCard(graphics, chip, Brand.AccentSoft, Brand.AccentSoft, false);
                    string label = target == ImageCopyTarget.Image ? "Image" : target == ImageCopyTarget.OcrText ? "Text" : "Base64";
                    string icon = target == ImageCopyTarget.Image ? "image" : target == ImageCopyTarget.OcrText ? "text" : "binary";
                    Color color = active ? Brand.HeaderBlue : Brand.TextSecondary;
                    Brand.DrawIcon(graphics, icon, new Rectangle(chip.Left + 6, chip.Top + 5, 12, 12), color);
                    TextRenderer.DrawText(graphics, label, active ? _fontBold80 : _fontRegular80,
                        new Rectangle(chip.Left + 21, chip.Top + 2, chip.Width - 24, chip.Height - 4), color,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                }
            }
            else
            {
                string kind = entry.IsSensitive ? "PROTECTED" : entry.Kind ?? "TEXT";
                Rectangle glyph = new Rectangle(card.Left + 10, card.Top + 4, 29, 28);
                DrawRoundedCard(graphics, glyph, selected ? Brand.AccentSoft : Brand.Canvas,
                    selected ? Brand.AccentSoft : Brand.Canvas, false);
                string icon = kind == "LINK" ? "link" : kind == "CODE" ? "code" : "text";
                Brand.DrawIcon(graphics, icon, new Rectangle(glyph.Left + 6, glyph.Top + 6, 16, 16),
                    selected ? Brand.HeaderBlue : Brand.TextMuted);
                TextRenderer.DrawText(graphics, entry.IsSensitive ? "Protected content removed" : CompactSnippet(entry.Text, 240),
                    kind == "CODE" ? _fontPreviewCode : _fontRegular90,
                    new Rectangle(textLeft, card.Top + 7, titleWidth, 22), Brand.TextPrimary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }

        private static string CompactSnippet(string value, int maxLength)
        {
            string snippet = value ?? "";
            if (snippet.Length > maxLength) snippet = snippet.Substring(0, maxLength);
            return snippet.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
        }
        private void DrawClipSnippet(Graphics graphics, string text, Rectangle bounds)
        {
            string snippet = text ?? "";
            if (snippet.Length > 240) snippet = snippet.Substring(0, 240);
            snippet = snippet.Replace("\r\n", " ").Replace("\n", " ").Replace("\t", " ");
            TextRenderer.DrawText(graphics, snippet, _fontRegular90, bounds, Brand.TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }

        private void OnListBoxSelectedIndexChanged(object sender, EventArgs e)
        {
            if (!_changingListItems) UpdateDetailPane();
        }


        private static string FormatRelativeTime(DateTime utcTime)
        {
            TimeSpan diff = DateTime.UtcNow - utcTime;
            if (diff.TotalSeconds < 45) return "just now";
            if (diff.TotalMinutes < 60) return string.Format("{0}m ago", (int)diff.TotalMinutes);
            if (diff.TotalHours < 24) return string.Format("{0}h ago", (int)diff.TotalHours);
            if (diff.TotalDays < 7) return string.Format("{0}d ago", (int)diff.TotalDays);
            return utcTime.ToLocalTime().ToString("MMM d");
        }

        private void OnPreviewTimerTick(object sender, EventArgs e)
        {
            if (_previewTimer != null)
            {
                _previewTimer.Stop();
                _previewTimer.Dispose();
                _previewTimer = null;
            }

            try
            {
                if (string.IsNullOrEmpty(_previewOutputPath))
                {
                    throw new InvalidOperationException("Preview output path was not specified.");
                }

                string dir = Path.GetDirectoryName(_previewOutputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                this.Show();
                this.Refresh();
                Application.DoEvents();

                int bmpWidth = Math.Max(1, this.Width);
                int bmpHeight = Math.Max(1, this.Height);
                using (Bitmap bmp = new Bitmap(bmpWidth, bmpHeight))
                {
                    this.DrawToBitmap(bmp, new Rectangle(0, 0, bmpWidth, bmpHeight));
                    bmp.Save(_previewOutputPath, System.Drawing.Imaging.ImageFormat.Png);
                }

                FileInfo fi = new FileInfo(_previewOutputPath);
                if (!fi.Exists || fi.Length == 0)
                {
                    throw new IOException("Preview screenshot file was not created or is empty.");
                }

                _previewRenderSucceeded = true;
                _previewRenderError = null;
            }
            catch (Exception ex)
            {
                _previewRenderSucceeded = false;
                _previewRenderError = ex.Message;
                Console.Error.WriteLine("Error generating preview screenshot: " + ex.Message);
            }
            finally
            {
                _isQuitting = true;
                this.Close();
                Application.Exit();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_isQuitting && !_isPreviewMode && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
                return;
            }

            base.OnFormClosing(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == _wmShowInstance && _wmShowInstance != 0)
            {
                ShowFromTray();
                return;
            }
            if (m.Msg == 0x0083) // WM_NCCALCSIZE: the custom frame occupies the full client area.
            {
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == 0x0084 && WindowState == FormWindowState.Normal) // WM_NCHITTEST
            {
                long position = m.LParam.ToInt64();
                Point point = PointToClient(new Point((short)(position & 0xffff), (short)((position >> 16) & 0xffff)));
                int hit = GetFrameHitTest(point, ClientSize);
                if (hit != 1) { m.Result = new IntPtr(hit); return; }
            }
            base.WndProc(ref m);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                // Preserve native sizing and minimum-size enforcement without a native caption.
                parameters.Style |= 0x00040000; // WS_THICKFRAME
                parameters.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                return parameters;
            }
        }

        internal static int GetFrameHitTest(Point point, Size size)
        {
            const int edge = 5;
            bool left = point.X < edge;
            bool right = point.X >= size.Width - edge;
            bool top = point.Y < edge;
            bool bottom = point.Y >= size.Height - edge;
            if (top && left) return 13;
            if (top && right) return 14;
            if (bottom && left) return 16;
            if (bottom && right) return 17;
            if (left) return 10;
            if (right) return 11;
            if (top) return 12;
            if (bottom) return 15;
            return 1;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            int headerBottom = _pnlSearchFilter == null ? 97 : _pnlSearchFilter.Bottom + Padding.Top;
            using (Brush blue = new SolidBrush(Brand.HeaderBlue))
                e.Graphics.FillRectangle(blue, 0, 0, Width, headerBottom);
            if (_pnlDetailToolbar != null && _pnlStatusBar != null)
            {
                int footerTop = _pnlDetailToolbar.Top + Padding.Top;
                using (Brush surface = new SolidBrush(Brand.Surface))
                    e.Graphics.FillRectangle(surface, 0, footerTop, Width, Math.Max(0, Height - footerTop));
            }
            if (Width > 1 && Height > 1)
                using (Pen border = new Pen(Brand.Border)) e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate();
            if (_listBoxClips != null) _listBoxClips.Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                _isDisposed = true;

                if (disposing)
                {
                    if (_history != null)
                    {
                        _history.Changed -= OnHistoryChanged;
                    }

                    if (_previewTimer != null)
                    {
                        _previewTimer.Stop();
                        _previewTimer.Dispose();
                        _previewTimer = null;
                    }

                    if (_pruneTimer != null)
                    {
                        _pruneTimer.Stop();
                        _pruneTimer.Dispose();
                        _pruneTimer = null;
                    }

                    if (_statusTimer != null)
                    {
                        _statusTimer.Stop();
                        _statusTimer.Dispose();
                        _statusTimer = null;
                    }

                    if (_toastTimer != null)
                    {
                        _toastTimer.Stop();
                        _toastTimer.Dispose();
                        _toastTimer = null;
                    }

                    if (_notifyIcon != null)
                    {
                        _notifyIcon.Visible = false;
                        _notifyIcon.Dispose();
                        _notifyIcon = null;
                    }

                    if (_trayMenu != null)
                    {
                        _trayMenu.Dispose();
                        _trayMenu = null;
                    }

                    if (_formIcon != null)
                    {
                        _formIcon.Dispose();
                        _formIcon = null;
                    }

                    if (_toolTip != null) _toolTip.Dispose();
                    Image previewImage = _picImagePreview != null ? _picImagePreview.Image : null;
                    if (_picImagePreview != null) _picImagePreview.Image = null;
                    if (previewImage != null) previewImage.Dispose();
                    ClearImageThumbnails();
                    DisposeCachedFonts();
                }
            }

            base.Dispose(disposing);
        }

        private void DisposeCachedFonts()
        {
            if (_fontPreviewRegular != null) { _fontPreviewRegular.Dispose(); _fontPreviewRegular = null; }
            if (_fontPreviewCode != null) { _fontPreviewCode.Dispose(); _fontPreviewCode = null; }
            if (_fontPreviewItalic != null) { _fontPreviewItalic.Dispose(); _fontPreviewItalic = null; }
            if (_fontHeaderTitle != null) { _fontHeaderTitle.Dispose(); _fontHeaderTitle = null; }
            if (_fontNavBold != null) { _fontNavBold.Dispose(); _fontNavBold = null; }
            if (_fontRegular90 != null) { _fontRegular90.Dispose(); _fontRegular90 = null; }
            if (_fontBold90 != null) { _fontBold90.Dispose(); _fontBold90 = null; }
            if (_fontRegular85 != null) { _fontRegular85.Dispose(); _fontRegular85 = null; }
            if (_fontBold85 != null) { _fontBold85.Dispose(); _fontBold85 = null; }
            if (_fontRegular80 != null) { _fontRegular80.Dispose(); _fontRegular80 = null; }
            if (_fontBold80 != null) { _fontBold80.Dispose(); _fontBold80 = null; }
            if (_fontBold75 != null) { _fontBold75.Dispose(); _fontBold75 = null; }
        }

        // --- Internal Test Seams for UiTests ---
        internal TextBox SearchTextBox { get { return _txtSearch; } }
        internal ListBox ClipsListBox { get { return _listBoxClips; } }
        internal TextBox PreviewTextBox { get { return _txtPreview; } }
        internal PictureBox ImagePreviewBox { get { return _picImagePreview; } }
        internal Panel ImageActionsPanel { get { return _pnlImageActions; } }
        internal Panel OcrPreviewPanel { get { return _pnlOcrPreview; } }
        internal TextBox OcrPreviewTextBox { get { return _txtOcrPreview; } }
        internal Button CopyImageBase64Button { get { return _btnCopyImageBase64; } }
        internal Button CopyImageTextButton { get { return _btnCopyImageText; } }
        internal int CachedImageThumbnailCount { get { return _imageThumbnails.Count; } }
        internal Button CopyButton { get { return _btnCopy; } }
        internal Button PinButton { get { return _btnPin; } }
        internal Button DeleteButton { get { return _btnDelete; } }
        internal Button PauseResumeButton { get { return _btnPauseResume; } }
        internal Button SettingsButton { get { return _btnSettings; } }
        internal Button CloseButton { get { return _btnClose; } }
        internal Rectangle HeaderSheepBounds { get { return new Rectangle(7, 0, 36, 36); } }
        internal Panel TypeFilterPanel { get { return _pnlTypeFilters; } }
        internal Button FilterAllButton { get { return _btnFilterAll; } }
        internal Button FilterTextButton { get { return _btnFilterText; } }
        internal Button FilterLinkButton { get { return _btnFilterLink; } }
        internal Button FilterCodeButton { get { return _btnFilterCode; } }
        internal Button FilterImageButton { get { return _btnFilterImage; } }
        internal Button PreviewToggleButton { get { return _btnPreview; } }
        internal Button NavAllButton { get { return _btnNavAll; } }
        internal Button NavPinnedButton { get { return _btnNavPinned; } }
        internal Panel DetailPanel { get { return _pnlDetail; } }
        internal Panel EmptyStatePanel { get { return _pnlEmptyState; } }
        internal Label HeaderTitleLabel { get { return _lblHeaderTitle; } }
        internal Panel MainPanel { get { return _pnlMain; } }
        internal Panel HeaderPanel { get { return _pnlHeader; } }
        internal Panel SearchFilterPanel { get { return _pnlSearchFilter; } }
        internal Panel ContentPanel { get { return _pnlContent; } }
        internal Panel ListContainerPanel { get { return _pnlListContainer; } }
        internal Panel DetailContentPanel { get { return _pnlDetailContent; } }
        internal Panel DetailToolbarPanel { get { return _pnlDetailToolbar; } }
        internal Panel DetailMetaPanel { get { return _pnlDetailMeta; } }
        internal Panel DetailTextContainerPanel { get { return _pnlTextContainer; } }
        internal Panel StatusBarPanel { get { return _pnlStatusBar; } }
        internal Label StatusLeftLabel { get { return _lblStatusLeft; } }
        internal Label StatusRightLabel { get { return _lblStatusRight; } }
        internal Label DetailKindLabel { get { return _lblDetailKind; } }
        internal Label DetailTimeLabel { get { return _lblDetailTime; } }
        internal Label CharCountLabel { get { return _lblCharCount; } }

        internal void TestApplySearch(string query)
        {
            _txtSearch.Text = query;
            ApplyFilters();
        }

        internal void TestSelectClip(int index)
        {
            if (index >= 0 && index < _listBoxClips.Items.Count)
            {
                _listBoxClips.SelectedIndex = index;
            }
            else
            {
                _listBoxClips.SelectedIndex = -1;
            }
        }

        internal void TestTogglePinSelected()
        {
            TogglePinSelectedClip();
        }

        internal void TestDeleteSelected()
        {
            DeleteSelectedClip();
        }

        internal void TestSetFilterKind(string kind)
        {
            _currentKindFilter = kind;
            UpdateFilterPillStyles();
            ApplyFilters();
        }

        internal void TestRefresh()
        {
            RefreshClipsList();
        }
    }
}
