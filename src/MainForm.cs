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
        private Button _btnPauseResume;

        private Panel _pnlSearchFilter;
        private TextBox _txtSearch;
        private Button _btnFilterAll;
        private Button _btnFilterText;
        private Button _btnFilterLink;
        private Button _btnFilterCode;

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

            _fontHeaderTitle = Brand.CreateFont(11f, FontStyle.Bold);
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
            this.FormBorderStyle = FormBorderStyle.SizableToolWindow;
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
            ClampWindowSize(workArea, new Size(420, 520), new Size(380, 420), out initialSize, out minSize);
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
            _pnlMain = new Panel { Dock = DockStyle.Fill, BackColor = Brand.WarmWoolCanvas };
            _pnlHeader = new Panel { Dock = DockStyle.Top, Height = 48 };
            _pnlHeader.Paint += delegate(object sender, PaintEventArgs e)
            {
                Brand.DrawSheep(e.Graphics, new RectangleF(12, 8, 32, 32));
            };
            _lblHeaderTitle = new Label { Text = "Dolly Paste", Font = _fontHeaderTitle,
                ForeColor = Brand.TextPrimary, Location = new Point(50, 14), AutoSize = true };
            _btnPauseResume = CreateActionButton("Pause", 62, OnPauseResumeClicked);
            _btnPauseResume.AccessibleName = "Toggle pause recording";
            _btnSettings = CreateActionButton("Settings", 72, delegate { OpenSettings(); });
            _btnSettings.AccessibleName = "Open settings dialog";
            _pnlHeader.Controls.AddRange(new Control[] { _lblHeaderTitle, _btnPauseResume, _btnSettings });
            _pnlHeader.Resize += delegate { UpdateHeaderLayout(); };

            _pnlSearchFilter = new Panel { Dock = DockStyle.Top, Height = 80 };
            _txtSearch = new PrivacyTextBox(_isDemoMode || _isPreviewMode, delegate { CopySelectedClip(); }, true);
            _txtSearch.SetBounds(12, 4, ClientSize.Width - 24, 26);
            _txtSearch.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            _txtSearch.Font = _fontPreviewRegular;
            _txtSearch.AccessibleName = "Search clips";
            _txtSearch.TextChanged += delegate { ApplyFilters(); };
            _txtSearch.HandleCreated += delegate
            {
                SendMessage(_txtSearch.Handle, 0x1501, new IntPtr(1), "Search clipboard history...");
            };
            _pnlSearchFilter.Controls.Add(_txtSearch);
            _pnlSearchFilter.Resize += delegate
            {
                _txtSearch.Width = Math.Max(1, _pnlSearchFilter.ClientSize.Width - 24);
            };

            _btnNavAll = CreateActionButton("All", 64, delegate
            {
                _currentNav = NavMode.All;
                UpdateNavStyles();
                ApplyFilters();
            });
            _btnNavPinned = CreateActionButton("Pinned", 72, delegate
            {
                _currentNav = NavMode.Pinned;
                UpdateNavStyles();
                ApplyFilters();
            });
            _btnNavAll.SetBounds(12, 44, 64, 28);
            _btnNavPinned.SetBounds(80, 44, 72, 28);
            _pnlSearchFilter.Controls.AddRange(new Control[] { _btnNavAll, _btnNavPinned });
            int filterX = 162;
            _btnFilterAll = CreateFilterPill("Any", null, 40, ref filterX);
            _btnFilterText = CreateFilterPill("Text", "TEXT", 42, ref filterX);
            _btnFilterLink = CreateFilterPill("Links", "LINK", 44, ref filterX);
            _btnFilterCode = CreateFilterPill("Code", "CODE", 44, ref filterX);
            _toolTip.SetToolTip(_btnFilterAll, "Show every content type");

            _pnlContent = new Panel { Dock = DockStyle.Fill };
            InitializeDetailPane();
            _pnlDetail.Dock = DockStyle.Bottom;
            _pnlDetail.Height = 168;
            _pnlDetail.Visible = false;
            // Actions stay reachable even when the optional preview is collapsed.
            _pnlDetailToolbar.Parent = _pnlMain;
            _pnlDetailToolbar.Dock = DockStyle.Bottom;
            _pnlDetailToolbar.Height = 48;
            _btnCopy.Text = "Copy";
            _btnCopy.SetBounds(12, 9, 78, 30);
            _btnPin.SetBounds(96, 9, 76, 30);
            _btnDelete.SetBounds(178, 9, 64, 30);
            _btnPreview = CreateActionButton("Preview", 80, delegate { TogglePreview(); });
            _btnPreview.SetBounds(248, 9, 80, 30);
            _btnPreview.AccessibleName = "Toggle clip preview";
            _pnlDetailToolbar.Controls.Add(_btnPreview);
            _toolTip.SetToolTip(_btnPreview, "Show or hide preview (Ctrl+Space)");
            _toolTip.SetToolTip(_btnPin, "Pin or unpin selected clip (Ctrl+P)");
            _toolTip.SetToolTip(_btnCopy, "Copy selected clip and close (Enter or Ctrl+C)");

            _pnlListContainer = new Panel { Dock = DockStyle.Fill };
            _listBoxClips = new ListBox { Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 54, BorderStyle = BorderStyle.None, BackColor = Brand.WarmWoolCanvas,
                IntegralHeight = false, AccessibleName = "Clipboard history items" };
            _listBoxClips.DrawItem += OnListBoxDrawItem;
            _listBoxClips.SelectedIndexChanged += OnListBoxSelectedIndexChanged;
            _listBoxClips.KeyDown += OnListBoxKeyDown;
            _listBoxClips.DoubleClick += delegate { CopySelectedClip(); };
            _toolTip.SetToolTip(_listBoxClips, "Double-click a clip to copy and close.");
            _pnlListContainer.Controls.Add(_listBoxClips);

            _pnlEmptyState = new Panel { Dock = DockStyle.Fill, BackColor = Brand.WarmWoolCanvas, Visible = false };
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

            _pnlStatusBar = new Panel { Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(12, 0, 12, 0) };
            _lblStatusLeft = new Label { Dock = DockStyle.Fill, Font = _fontRegular80,
                ForeColor = Brand.TextSecondary, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
            _lblStatusRight = new Label { Dock = DockStyle.Right, Width = 64, Font = _fontRegular80,
                ForeColor = Brand.TextSecondary, TextAlign = ContentAlignment.MiddleRight };
            _lblToast = new Label { Dock = DockStyle.Fill, Font = _fontBold80, ForeColor = Brand.MossGreen,
                AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Visible = false };
            _pnlStatusBar.Controls.AddRange(new Control[] { _lblStatusLeft, _lblToast, _lblStatusRight });
            _toolTip.SetToolTip(_pnlStatusBar, "Ctrl+Shift+V opens Dolly Paste. Clips stay on this device.");

            _pnlMain.Controls.Add(_pnlContent);
            _pnlMain.Controls.Add(_pnlSearchFilter);
            _pnlMain.Controls.Add(_pnlHeader);
            _pnlMain.Controls.SetChildIndex(_pnlContent, 0);
            _pnlMain.Controls.SetChildIndex(_pnlDetailToolbar, 1);
            _pnlMain.Controls.SetChildIndex(_pnlSearchFilter, 2);
            _pnlMain.Controls.SetChildIndex(_pnlHeader, 3);
            if (_isDemoMode || _isPreviewMode)
            {
                _pnlDemoBanner = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Brand.WarningBannerBg };
                _pnlDemoBanner.Controls.Add(new Label { Dock = DockStyle.Fill,
                    Text = "Preview mode — recording disabled", Font = _fontRegular80,
                    ForeColor = Brand.WarningBannerFg, TextAlign = ContentAlignment.MiddleCenter });
                _pnlMain.Controls.Add(_pnlDemoBanner);
            }
            Controls.Add(_pnlMain);
            Controls.Add(_pnlStatusBar);
            UpdateNavStyles();
            UpdateFilterPillStyles();
            UpdatePauseResumeState();
            ResumeLayout(true);
            UpdateHeaderLayout();
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, string lParam);

        private Button CreateActionButton(string text, int width, EventHandler action)
        {
            Button button = new Button { Text = text, Width = width, Height = 28,
                Font = _fontRegular85, FlatStyle = FlatStyle.Flat, BackColor = Brand.WarmWoolCanvas,
                ForeColor = Brand.TextPrimary, AccessibleName = text };
            button.FlatAppearance.BorderColor = Brand.Border;
            button.Click += action;
            return button;
        }

        private void UpdateHeaderLayout()
        {
            if (_btnSettings == null || _btnPauseResume == null) return;
            _btnSettings.Location = new Point(_pnlHeader.ClientSize.Width - 84, 10);
            _btnPauseResume.Location = new Point(_btnSettings.Left - 68, 10);
        }

        private void TogglePreview()
        {
            _previewExpanded = !_previewExpanded;
            _pnlDetail.Visible = _previewExpanded;
            _btnPreview.Text = _previewExpanded ? "Hide" : "Preview";
            _btnPreview.AccessibleDescription = _previewExpanded ? "Preview expanded" : "Preview collapsed";
            UpdatePreviewHeight();
        }

        private void UpdatePreviewHeight()
        {
            if (_pnlDetail != null && _pnlContent != null)
                _pnlDetail.Height = Math.Max(0, Math.Min(168, _pnlContent.ClientSize.Height - 100));
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
                    e.Graphics.DrawLine(p, 0, 0, 0, _pnlDetail.Height);
                }
            };

            // Empty detail state
            _pnlDetailEmpty = new Panel();
            _pnlDetailEmpty.Dock = DockStyle.Fill;
            _pnlDetailEmpty.BackColor = Color.White;

            Label lblDetailEmpty = new Label();
            lblDetailEmpty.Text = "Select a clip to preview its text";
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
                    e.Graphics.DrawLine(p, 12, _pnlDetailToolbar.Height - 1, _pnlDetailToolbar.Width - 12, _pnlDetailToolbar.Height - 1);
                }
            };

            _btnCopy = new Button();
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
            _pnlDetailToolbar.Controls.Add(_btnCopy);

            _btnPin = new Button();
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
            _pnlDetailToolbar.Controls.Add(_btnPin);

            _btnDelete = new Button();
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

            // Detail Content Order: Fill (_pnlTextContainer at 0), Top (_pnlDetailMeta at 1), Top (_pnlDetailToolbar at 2)
            _pnlDetailContent.Controls.Add(_pnlTextContainer);
            _pnlDetailContent.Controls.Add(_pnlDetailMeta);
            _pnlDetailContent.Controls.Add(_pnlDetailToolbar);
            _pnlDetailContent.Controls.SetChildIndex(_pnlTextContainer, 0);
            _pnlDetailContent.Controls.SetChildIndex(_pnlDetailMeta, 1);
            _pnlDetailContent.Controls.SetChildIndex(_pnlDetailToolbar, 2);

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

        private Button CreateFilterPill(string text, string kind, int width, ref int currentX)
        {
            Button btn = new Button();
            btn.Text = text;
            btn.Tag = kind;
            btn.Font = _fontRegular85;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.Size = new Size(width, 26);
            btn.Location = new Point(currentX, 45);
            btn.AccessibleName = string.Format("Filter by {0}", text);
            btn.Click += delegate
            {
                _currentKindFilter = kind;
                UpdateFilterPillStyles();
                ApplyFilters();
            };
            _pnlSearchFilter.Controls.Add(btn);
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
                        if (text.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }
                    }
                }

                filtered.Add(entry);
            }

            _displayedClips = filtered;

            _listBoxClips.BeginUpdate();
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

            _listBoxClips.EndUpdate();

            if (_displayedClips.Count == 0)
            {
                _pnlEmptyState.Visible = true;
                _pnlEmptyState.BringToFront();

                if (_clipboard != null && _clipboard.Paused)
                {
                    _lblEmptyTitle.Text = "Capture is paused";
                    _lblEmptySubtitle.Text = "Clipboard recording is currently paused.\nClick 'Resume' above to resume capturing clips.";
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
                    _lblEmptySubtitle.Text = "Copy text in any application to save it here.\nClips are stored 100% locally on this device.";
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
                _btnCopy.Enabled = false;
                _btnPin.Enabled = false;
                _btnDelete.Enabled = false;
                return;
            }

            ClipEntry clip = _displayedClips[_listBoxClips.SelectedIndex];
            Guid expectedId = clip.Id;
            _selectedClipId = expectedId;

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
            _btnPin.Enabled = true;
            _btnDelete.Enabled = true;

            DateTime localTime = clip.CapturedUtc.ToLocalTime();
            _lblDetailTime.Text = localTime.ToString("MMM d, yyyy h:mm tt");

            if (clip.IsSensitive)
            {
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
                    // Native multiline edit controls expect CRLF; keep stored/copied text unchanged.
                    _txtPreview.Text = fullText.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
                    if (string.Equals(kind, "CODE", StringComparison.OrdinalIgnoreCase))
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
        }

        private void CopySelectedClip()
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

            string text = _history.GetText(id);
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (_isDemoMode || _isPreviewMode)
            {
                ShowToast("Preview mode: copy simulated");
                return;
            }

            if (_clipboard != null)
            {
                bool success = _clipboard.CopyText(text, delegate(uint sequence)
                {
                    _history.TrackRestored(id, sequence);
                });

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

        }

        private void UpdateStatusBar()
        {
            if (_lblStatusLeft == null || _lblStatusRight == null) return;

            string statusLeft;
            if (_isDemoMode || _isPreviewMode)
            {
                statusLeft = "↑↓ choose · Enter copy · Esc close";
            }
            else if (_clipboard != null && !string.IsNullOrEmpty(_clipboard.Status) && _clipboard.Status != "Active")
            {
                statusLeft = _clipboard.Status;
            }
            else
            {
                statusLeft = "↑↓ choose · Enter copy · Esc close";
            }

            if (_history != null && !string.IsNullOrEmpty(_history.StorageWarning))
            {
                statusLeft = _history.StorageWarning + " | " + statusLeft;
            }

            _lblStatusLeft.Text = statusLeft;
            _toolTip.SetToolTip(_lblStatusLeft, statusLeft);

            int total = _allClips != null ? _allClips.Count : 0;
            _lblStatusRight.Text = string.Format("{0} clips", total);
        }

        private void UpdateNavStyles()
        {
            if (_btnNavAll == null || _btnNavPinned == null) return;
            _btnNavAll.Text = "All";
            _btnNavPinned.Text = "Pinned";
            ApplyFilterPillStyle(_btnNavAll, _currentNav == NavMode.All);
            ApplyFilterPillStyle(_btnNavPinned, _currentNav == NavMode.Pinned);
        }

        private void UpdateFilterPillStyles()
        {
            ApplyFilterPillStyle(_btnFilterAll, _currentKindFilter == null);
            ApplyFilterPillStyle(_btnFilterText, string.Equals(_currentKindFilter, "TEXT", StringComparison.OrdinalIgnoreCase));
            ApplyFilterPillStyle(_btnFilterLink, string.Equals(_currentKindFilter, "LINK", StringComparison.OrdinalIgnoreCase));
            ApplyFilterPillStyle(_btnFilterCode, string.Equals(_currentKindFilter, "CODE", StringComparison.OrdinalIgnoreCase));
        }

        private void ApplyFilterPillStyle(Button btn, bool active)
        {
            if (active)
            {
                btn.BackColor = Brand.MossGreen;
                btn.ForeColor = Color.White;
            }
            else
            {
                btn.BackColor = Color.FromArgb(240, 237, 230);
                btn.ForeColor = Brand.TextSecondary;
            }
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
            Rectangle workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size initialSize, minSize;
            ClampWindowSize(workArea, this.Size, new Size(380, 420), out initialSize, out minSize);
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

        private void OnListBoxDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _displayedClips.Count) return;

            ClipEntry item = _displayedClips[e.Index];
            bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Graphics g = e.Graphics;
            Rectangle bounds = e.Bounds;

            Color bgColor = isSelected ? Brand.CardSelected : (e.Index % 2 == 0 ? Color.White : Brand.WarmWoolCanvas);
            using (SolidBrush bgBrush = new SolidBrush(bgColor))
            {
                g.FillRectangle(bgBrush, bounds);
            }

            if (isSelected)
            {
                using (SolidBrush selBar = new SolidBrush(Brand.MossGreen))
                {
                    g.FillRectangle(selBar, bounds.X, bounds.Y, 4, bounds.Height);
                }
            }

            using (Pen divPen = new Pen(Brand.BorderLight, 1))
            {
                g.DrawLine(divPen, bounds.X + 8, bounds.Bottom - 1, bounds.Right - 8, bounds.Bottom - 1);
            }

            string kind = item.IsSensitive ? "PROTECTED" : (item.Kind ?? "TEXT");
            Color badgeBg = Brand.GetKindBadgeBg(kind);
            Color badgeFg = Brand.GetKindBadgeFg(kind);
            int badgeWidth = item.IsSensitive ? 86 : 54;
            Rectangle badgeRect = new Rectangle(bounds.X + 14, bounds.Y + 8, badgeWidth, 18);

            using (SolidBrush badgeBrush = new SolidBrush(badgeBg))
            {
                g.FillRectangle(badgeBrush, badgeRect);
            }
            using (SolidBrush badgeTextBrush = new SolidBrush(badgeFg))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(kind, _fontBold75, badgeTextBrush, badgeRect, sf);
            }

            int rightOffset = bounds.Right - 14;
            if (item.IsPinned)
            {
                using (SolidBrush pinBrush = new SolidBrush(Brand.MossGreen))
                {
                    g.DrawString("★", _fontNavBold, pinBrush, rightOffset - 16, bounds.Y + 8);
                }
                rightOffset -= 22;
            }

            string timeStr = FormatRelativeTime(item.CapturedUtc);
            using (SolidBrush timeBrush = new SolidBrush(Brand.TextSecondary))
            {
                SizeF timeSize = g.MeasureString(timeStr, _fontRegular80);
                g.DrawString(timeStr, _fontRegular80, timeBrush, rightOffset - timeSize.Width, bounds.Y + 9);
            }

            Rectangle textRect = new Rectangle(bounds.X + 14, bounds.Y + 29, bounds.Width - 28, 21);
            if (item.IsSensitive)
            {
                using (SolidBrush sensBrush = new SolidBrush(Brand.SensitiveFg))
                {
                    g.DrawString("[Protected content removed]", _fontPreviewItalic, sensBrush, textRect);
                }
            }
            else
            {
                string snippet = (item.Text ?? "").Replace("\r\n", " ").Replace("\n", " ").Replace("\t", " ");
                TextRenderer.DrawText(
                    g,
                    snippet,
                    _fontRegular90,
                    textRect,
                    Brand.TextPrimary,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            }
        }

        private void OnListBoxSelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateDetailPane();
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
            base.WndProc(ref m);
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
        internal Button CopyButton { get { return _btnCopy; } }
        internal Button PinButton { get { return _btnPin; } }
        internal Button DeleteButton { get { return _btnDelete; } }
        internal Button PauseResumeButton { get { return _btnPauseResume; } }
        internal Button SettingsButton { get { return _btnSettings; } }
        internal Button FilterAllButton { get { return _btnFilterAll; } }
        internal Button FilterTextButton { get { return _btnFilterText; } }
        internal Button FilterLinkButton { get { return _btnFilterLink; } }
        internal Button FilterCodeButton { get { return _btnFilterCode; } }
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
