using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DollyPaste
{
    /// <summary>
    /// Configuration dialog for clipboard retention and optional encrypted storage.
    /// Scales cleanly across DPI settings with scrollable content and reachable action buttons.
    /// </summary>
    public sealed class SettingsDialog : Form
    {
        private readonly Settings _settings;
        private readonly HistoryStore _history;
        private readonly string _dataDirectory;

        private readonly bool _isDemoMode;

        private Panel _pnlHeader;
        private Panel _pnlBottom;
        private Panel _pnlBody;
        private bool _layingOutBody;

        private Label _lblRetentionHeader;
        private NumericUpDown _numMaxAge;
        private Label _lblMaxAgeHelper;
        private Label _lblPinExpiryNotice;

        private Label _lblMaxItemsHeader;
        private NumericUpDown _numMaxItems;
        private Label _lblMaxItemsHelp;

        private CheckBox _chkPersistHistory;

        private Panel _pnlNotice;
        private Label _lblNoticeTitle;
        private Label _lblNoticeBody;

        private Button _btnClearHistory;
        private Button _btnSave;
        private Button _btnCancel;
        private Icon _dialogIcon;

        // Cached fonts
        private Font _fontTitle;
        private Font _fontSubtitle;
        private Font _fontSectionHeader;
        private Font _fontRegular;
        private Font _fontBold;
        private Font _fontSmall;
        private Font _fontItalic;
        private Font _fontNoticeTitle;

        public SettingsDialog(Settings settings, HistoryStore history, string dataDirectory)
            : this(settings, history, dataDirectory, false)
        {
        }

        public SettingsDialog(Settings settings, HistoryStore history, string dataDirectory, bool isDemoMode)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            _settings = settings.Clone();
            _history = history;
            _dataDirectory = dataDirectory;
            _isDemoMode = isDemoMode;

            InitializeFonts();
            InitializeComponent();
            LoadCurrentSettings();

            if (_isDemoMode)
            {
                SuppressClipboardOnControl(this);
            }
        }

        private void InitializeFonts()
        {
            _fontTitle = Brand.CreateFont(13f, FontStyle.Bold);
            _fontSubtitle = Brand.CreateFont(8.5f, FontStyle.Regular);
            _fontSectionHeader = Brand.CreateFont(10.0f, FontStyle.Bold);
            _fontRegular = Brand.CreateFont(9.5f, FontStyle.Regular);
            _fontBold = Brand.CreateFont(9.5f, FontStyle.Bold);
            _fontSmall = Brand.CreateFont(8.5f, FontStyle.Regular);
            _fontItalic = Brand.CreateFont(8.5f, FontStyle.Regular);
            _fontNoticeTitle = Brand.CreateFont(9.0f, FontStyle.Bold);
        }

        private void InitializeComponent()
        {
            this.AutoScaleMode = AutoScaleMode.None;
            this.Text = "Settings - Dolly Paste";

            Screen screen = Screen.PrimaryScreen;
            try
            {
                screen = Screen.FromControl(this) ?? Screen.PrimaryScreen;
            }
            catch
            {
            }
            Rectangle workArea = (screen != null) ? screen.WorkingArea : Screen.PrimaryScreen.WorkingArea;
            Size initialSize, minSize;
            MainForm.ClampWindowSize(workArea, new Size(560, 620), new Size(520, 520), out initialSize, out minSize);
            this.Size = initialSize;
            this.MinimumSize = minSize;

            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.BackColor = Brand.Canvas;
            this.Font = _fontRegular;
            _dialogIcon = Brand.CreateAppIcon();
            this.Icon = _dialogIcon;

            // 1. Header Panel (Dock Top)
            _pnlHeader = new Panel();
            _pnlHeader.Dock = DockStyle.Top;
            _pnlHeader.Height = 72;
            _pnlHeader.BackColor = Brand.Surface;
            _pnlHeader.Paint += delegate(object sender, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = Brand.CreateRoundedPath(new RectangleF(16, 12, 48, 48), 14))
                using (SolidBrush brush = new SolidBrush(Brand.AccentSoft))
                    e.Graphics.FillPath(brush, path);
                Brand.DrawSheep(e.Graphics, new RectangleF(20, 16, 40, 40));
                using (Pen p = new Pen(Brand.BorderLight, 1))
                {
                    e.Graphics.DrawLine(p, 0, _pnlHeader.Height - 1, _pnlHeader.Width, _pnlHeader.Height - 1);
                }
            };

            Label lblTitle = new Label();
            lblTitle.Text = "Settings";
            lblTitle.Font = _fontTitle;
            lblTitle.ForeColor = Brand.TextPrimary;
            lblTitle.Location = new Point(76, 14);
            lblTitle.AutoSize = true;
            _pnlHeader.Controls.Add(lblTitle);

            Label lblSubtitle = new Label();
            lblSubtitle.Text = "Dolly Paste · Local history, your preferences";
            lblSubtitle.Font = _fontSubtitle;
            lblSubtitle.ForeColor = Brand.TextSecondary;
            lblSubtitle.Location = new Point(77, 40);
            lblSubtitle.AutoSize = true;
            _pnlHeader.Controls.Add(lblSubtitle);

            // 2. Bottom Actions Bar (Dock Bottom - always reachable outside scroll body)
            _pnlBottom = new Panel();
            _pnlBottom.Dock = DockStyle.Bottom;
            _pnlBottom.Height = 54;
            _pnlBottom.BackColor = Brand.Surface;
            _pnlBottom.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen p = new Pen(Brand.BorderLight, 1))
                {
                    e.Graphics.DrawLine(p, 0, 0, _pnlBottom.Width, 0);
                }
            };

            _btnSave = new BrandButton();
            _btnSave.Text = "Save Settings";
            _btnSave.Font = _fontBold;
            Brand.StyleButton(_btnSave, true);
            _btnSave.Size = new Size(128, 34);
            _btnSave.AccessibleName = "Save settings";
            _btnSave.Click += OnSaveClicked;
            _pnlBottom.Controls.Add(_btnSave);

            _btnCancel = new BrandButton();
            _btnCancel.Text = "Cancel";
            _btnCancel.Font = _fontRegular;
            Brand.StyleButton(_btnCancel, false);
            _btnCancel.Size = new Size(95, 34);
            _btnCancel.AccessibleName = "Cancel settings";
            _btnCancel.Click += delegate { this.DialogResult = DialogResult.Cancel; this.Close(); };
            _pnlBottom.Controls.Add(_btnCancel);

            _pnlBottom.Resize += delegate
            {
                UpdateBottomButtonsLayout();
            };
            UpdateBottomButtonsLayout();

            // 3. Scrollable Content Body (Dock Fill)
            _pnlBody = new Panel();
            _pnlBody.Dock = DockStyle.Fill;
            _pnlBody.AutoScroll = true;
            _pnlBody.Padding = new Padding(16, 12, 16, 12);
            _pnlBody.BackColor = Brand.Canvas;

            // Controls inside body
            _lblRetentionHeader = new Label();
            _lblRetentionHeader.Text = "Keep clips for (minutes)";
            _lblRetentionHeader.Font = _fontSectionHeader;
            _lblRetentionHeader.ForeColor = Brand.TextPrimary;
            _lblRetentionHeader.AutoSize = true;
            _pnlBody.Controls.Add(_lblRetentionHeader);

            _numMaxAge = new NumericUpDown();
            _numMaxAge.Size = new Size(110, 26);
            _numMaxAge.Minimum = 1;
            _numMaxAge.Maximum = 43200;
            _numMaxAge.Increment = 60;
            _numMaxAge.AccessibleName = "Retention minutes";
            _numMaxAge.BackColor = Brand.Surface;
            _numMaxAge.ForeColor = Brand.TextPrimary;
            _numMaxAge.BorderStyle = BorderStyle.FixedSingle;
            _numMaxAge.ValueChanged += OnMaxAgeChanged;
            _pnlBody.Controls.Add(_numMaxAge);

            _lblMaxAgeHelper = new Label();
            _lblMaxAgeHelper.Font = _fontRegular;
            _lblMaxAgeHelper.ForeColor = Brand.AccentHover;
            _pnlBody.Controls.Add(_lblMaxAgeHelper);

            _lblPinExpiryNotice = new Label();
            _lblPinExpiryNotice.Text = "Pinned clips follow the same retention limits.";
            _lblPinExpiryNotice.Font = _fontItalic;
            _lblPinExpiryNotice.ForeColor = Brand.TextSecondary;
            _pnlBody.Controls.Add(_lblPinExpiryNotice);

            _lblMaxItemsHeader = new Label();
            _lblMaxItemsHeader.Text = "History capacity (clips)";
            _lblMaxItemsHeader.Font = _fontSectionHeader;
            _lblMaxItemsHeader.ForeColor = Brand.TextPrimary;
            _lblMaxItemsHeader.AutoSize = true;
            _pnlBody.Controls.Add(_lblMaxItemsHeader);

            _numMaxItems = new NumericUpDown();
            _numMaxItems.Size = new Size(110, 26);
            _numMaxItems.Minimum = 10;
            _numMaxItems.Maximum = 1000;
            _numMaxItems.Increment = 10;
            _numMaxItems.AccessibleName = "Maximum items";
            _numMaxItems.BackColor = Brand.Surface;
            _numMaxItems.ForeColor = Brand.TextPrimary;
            _numMaxItems.BorderStyle = BorderStyle.FixedSingle;
            _pnlBody.Controls.Add(_numMaxItems);

            _lblMaxItemsHelp = new Label();
            _lblMaxItemsHelp.Text = "10–1,000 clips. Oldest unpinned clips go first.";
            _lblMaxItemsHelp.Font = _fontSubtitle;
            _lblMaxItemsHelp.ForeColor = Brand.TextSecondary;
            _pnlBody.Controls.Add(_lblMaxItemsHelp);

            _chkPersistHistory = new CheckBox();
            _chkPersistHistory.Text = "Remember history after quitting (encrypted)";
            _chkPersistHistory.Font = _fontRegular;
            _chkPersistHistory.ForeColor = Brand.TextPrimary;
            _chkPersistHistory.AccessibleName = "Remember history after quitting encrypted";
            _pnlBody.Controls.Add(_chkPersistHistory);

            // Privacy Notice / Caveats Card
            _pnlNotice = new Panel();
            _pnlNotice.BackColor = Brand.Canvas;
            _pnlNotice.Paint += delegate(object sender, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = Brand.CreateRoundedPath(new RectangleF(0.5f, 0.5f,
                    _pnlNotice.Width - 1, _pnlNotice.Height - 1), 12))
                using (SolidBrush brush = new SolidBrush(Brand.Surface))
                using (Pen p = new Pen(Brand.BorderLight, 1))
                {
                    e.Graphics.FillPath(brush, path);
                    e.Graphics.DrawPath(p, path);
                }
            };

            _lblNoticeTitle = new Label();
            _lblNoticeTitle.Text = "Local by design";
            _lblNoticeTitle.Font = _fontNoticeTitle;
            _lblNoticeTitle.ForeColor = Brand.TextPrimary;
            _lblNoticeTitle.BackColor = Brand.Surface;
            _lblNoticeTitle.AutoSize = true;
            _pnlNotice.Controls.Add(_lblNoticeTitle);

            _lblNoticeBody = new Label();
            _lblNoticeBody.Text = "• History stays on this PC, in memory unless encrypted storage is enabled.\n" +
                                 "• Images copy as pictures by default; OCR text and Base64 are separate choices. Text recognition runs locally through Windows.\n" +
                                 "• Copying does not remove saved clips. Content excluded by its source app is not recorded.\n" +
                                 "• Windows clipboard history (Win+V) is managed separately. Removed data uses normal memory cleanup, not secure erasure.";
            _lblNoticeBody.Font = _fontSmall;
            _lblNoticeBody.ForeColor = Brand.TextSecondary;
            _lblNoticeBody.BackColor = Brand.Surface;
            _pnlNotice.Controls.Add(_lblNoticeBody);

            _pnlBody.Controls.Add(_pnlNotice);

            _btnClearHistory = new BrandButton();
            _btnClearHistory.Text = "Clear All History...";
            _btnClearHistory.Font = _fontSubtitle;
            Brand.StyleButton(_btnClearHistory, false);
            _btnClearHistory.ForeColor = Brand.SensitiveFg;
            _btnClearHistory.FlatAppearance.MouseOverBackColor = Brand.SensitiveBg;
            _btnClearHistory.FlatAppearance.MouseDownBackColor = Brand.SensitiveBg;
            _btnClearHistory.Size = new Size(160, 32);
            _btnClearHistory.AccessibleName = "Clear all clipboard history";
            _btnClearHistory.Click += OnClearHistoryClicked;
            _pnlBody.Controls.Add(_btnClearHistory);

            _pnlBody.Resize += delegate { LayoutBody(); };
            LayoutBody();

            // Reverse child-index docking order: Fill at index 0, edges higher indices
            this.Controls.Add(_pnlBody);
            this.Controls.Add(_pnlBottom);
            this.Controls.Add(_pnlHeader);
            this.Controls.SetChildIndex(_pnlBody, 0);
            this.Controls.SetChildIndex(_pnlBottom, 1);
            this.Controls.SetChildIndex(_pnlHeader, 2);

            this.AcceptButton = _btnSave;
            this.CancelButton = _btnCancel;
        }

        private void LayoutBody()
        {
            if (_pnlBody == null || _lblPinExpiryNotice == null || _layingOutBody) return;

            _layingOutBody = true;
            _pnlBody.SuspendLayout();
            try
            {

            // Reserve the scrollbar gutter so a smaller window only scrolls
            // vertically instead of gaining a horizontal bar during reflow.
            int availWidth = Math.Max(260, _pnlBody.ClientSize.Width - 36 - SystemInformation.VerticalScrollBarWidth);
            int currentY = 12;

            _lblRetentionHeader.Location = new Point(14, currentY);
            currentY += 24;

            _numMaxAge.Location = new Point(16, currentY);
            int helperWidth = Math.Max(80, availWidth - 120);
            _lblMaxAgeHelper.Location = new Point(136, currentY + 3);
            _lblMaxAgeHelper.Size = new Size(helperWidth, 22);
            _lblMaxAgeHelper.AutoEllipsis = true;
            currentY += 32;

            _lblPinExpiryNotice.Location = new Point(16, currentY);
            _lblPinExpiryNotice.MaximumSize = new Size(availWidth, 0);
            _lblPinExpiryNotice.AutoSize = true;
            currentY += _lblPinExpiryNotice.Height + 10;

            _lblMaxItemsHeader.Location = new Point(14, currentY);
            currentY += 24;

            _numMaxItems.Location = new Point(16, currentY);
            _lblMaxItemsHelp.Location = new Point(136, currentY + 3);
            _lblMaxItemsHelp.Size = new Size(helperWidth, 22);
            _lblMaxItemsHelp.AutoEllipsis = true;
            currentY += 36;

            _chkPersistHistory.Location = new Point(16, currentY);
            _chkPersistHistory.Size = new Size(availWidth, 24);
            _chkPersistHistory.AutoEllipsis = true;
            currentY += 34;

            // Privacy Notice / Caveats Card
            _pnlNotice.Location = new Point(16, currentY);
            _pnlNotice.Width = availWidth;
            _lblNoticeTitle.Location = new Point(14, 12);
            _lblNoticeBody.Location = new Point(14, 36);
            _lblNoticeBody.MaximumSize = new Size(availWidth - 28, 0);
            _lblNoticeBody.AutoSize = true;
            int noticeBodyHeight = _lblNoticeBody.PreferredHeight;
            _lblNoticeBody.Size = new Size(availWidth - 28, Math.Max(60, noticeBodyHeight));
            _pnlNotice.Height = _lblNoticeBody.Bottom + 14;
            currentY += _pnlNotice.Height + 16;

            _btnClearHistory.Location = new Point(16, currentY);
            currentY += _btnClearHistory.Height + 16;

            _pnlBody.AutoScrollMinSize = new Size(0, currentY);
            }
            finally
            {
                _pnlBody.ResumeLayout(true);
                // Recompute scroll extents after the resize-triggered layout
                // has applied all child bounds, removing any stale horizontal bar.
                _pnlBody.PerformLayout();
                _layingOutBody = false;
            }
        }

        private void UpdateBottomButtonsLayout()
        {
            if (_pnlBottom == null || _btnCancel == null || _btnSave == null) return;
            int btnY = Math.Max(0, (_pnlBottom.ClientSize.Height - _btnCancel.Height) / 2);
            _btnCancel.Location = new Point(_pnlBottom.ClientSize.Width - _btnCancel.Width - 16, btnY);
            _btnSave.Location = new Point(_btnCancel.Left - _btnSave.Width - 12, btnY);
        }

        private void SuppressClipboardOnControl(Control parent)
        {
            if (parent == null) return;
            TextBox tb = parent as TextBox;
            if (tb != null)
            {
                tb.ShortcutsEnabled = false;
                tb.ContextMenu = new ContextMenu();
            }
            NumericUpDown nud = parent as NumericUpDown;
            if (nud != null)
            {
                for (int i = 0; i < nud.Controls.Count; i++)
                {
                    TextBox childTb = nud.Controls[i] as TextBox;
                    if (childTb != null)
                    {
                        childTb.ShortcutsEnabled = false;
                        childTb.ContextMenu = new ContextMenu();
                    }
                }
            }
            for (int i = 0; i < parent.Controls.Count; i++)
            {
                SuppressClipboardOnControl(parent.Controls[i]);
            }
        }

        private void LoadCurrentSettings()
        {
            _numMaxAge.Value = Math.Max(1, Math.Min(43200, _settings.MaxAgeMinutes));
            _numMaxItems.Value = Math.Max(10, Math.Min(1000, _settings.MaxItems));
            _chkPersistHistory.Checked = _settings.PersistHistory;
            UpdateMaxAgeHelper();
        }

        private void OnMaxAgeChanged(object sender, EventArgs e)
        {
            UpdateMaxAgeHelper();
        }

        private void UpdateMaxAgeHelper()
        {
            int minutes = (int)_numMaxAge.Value;
            string text;
            if (minutes < 60)
            {
                text = string.Format("= {0} minutes", minutes);
            }
            else if (minutes < 1440)
            {
                double hours = minutes / 60.0;
                text = string.Format("= {0:0.#} hours", hours);
            }
            else
            {
                double days = minutes / 1440.0;
                double hours = minutes / 60.0;
                text = string.Format("= {0:0.#} days ({1:0.#} hours)", days, hours);
            }
            _lblMaxAgeHelper.Text = text;
        }

        private void OnClearHistoryClicked(object sender, EventArgs e)
        {
            DialogResult res = MessageBox.Show(this,
                "Are you sure you want to permanently clear all clipboard history?\n\nThis will immediately remove all stored clips, including pinned clips.",
                "Clear History - Dolly Paste",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (res == DialogResult.Yes)
            {
                if (_history != null)
                {
                    _history.Clear();
                }
            }
        }

        private void OnSaveClicked(object sender, EventArgs e)
        {
            _settings.MaxAgeMinutes = (int)_numMaxAge.Value;
            _settings.MaxItems = (int)_numMaxItems.Value;
            _settings.PersistHistory = _chkPersistHistory.Checked;

            _settings.Normalize();

            if (_history != null)
            {
                _history.UpdateSettings(_settings);
            }

            if (!string.IsNullOrEmpty(_dataDirectory))
            {
                try
                {
                    SettingsStore.Save(_dataDirectory, _settings);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this,
                        string.Format("Failed to save settings to disk: {0}", ex.Message),
                        "Dolly Paste",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }
            }

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_dialogIcon != null)
                {
                    _dialogIcon.Dispose();
                    _dialogIcon = null;
                }
                if (_fontTitle != null) { _fontTitle.Dispose(); _fontTitle = null; }
                if (_fontSubtitle != null) { _fontSubtitle.Dispose(); _fontSubtitle = null; }
                if (_fontSectionHeader != null) { _fontSectionHeader.Dispose(); _fontSectionHeader = null; }
                if (_fontRegular != null) { _fontRegular.Dispose(); _fontRegular = null; }
                if (_fontBold != null) { _fontBold.Dispose(); _fontBold = null; }
                if (_fontSmall != null) { _fontSmall.Dispose(); _fontSmall = null; }
                if (_fontItalic != null) { _fontItalic.Dispose(); _fontItalic = null; }
                if (_fontNoticeTitle != null) { _fontNoticeTitle.Dispose(); _fontNoticeTitle = null; }
            }
            base.Dispose(disposing);
        }

        // --- Internal Test Seams for UiTests ---
        internal Panel BodyPanel { get { return _pnlBody; } }
        internal Panel BottomPanel { get { return _pnlBottom; } }
        internal Panel HeaderPanel { get { return _pnlHeader; } }
        internal Button SaveButton { get { return _btnSave; } }
        internal Button CancelActionButton { get { return _btnCancel; } }
        internal Panel NoticePanel { get { return _pnlNotice; } }
        internal NumericUpDown MaxAgeNumeric { get { return _numMaxAge; } }
        internal NumericUpDown MaxItemsNumeric { get { return _numMaxItems; } }
        internal CheckBox PersistHistoryCheckBox { get { return _chkPersistHistory; } }
        internal Label PinExpiryNoticeLabel { get { return _lblPinExpiryNotice; } }
        internal Label NoticeBodyLabel { get { return _lblNoticeBody; } }
    }
}
