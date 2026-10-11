using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DollyPaste.Tests
{
    /// <summary>
    /// Headless UI and layout integration tests for Dolly Paste desktop form.
    /// Uses synthetic demo fixtures; the production listener seam starts paused without reading clipboard payloads.
    /// Strictly C# 5.0 compatible.
    /// </summary>
    public static class UiTests
    {
        [STAThread]
        public static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Console.WriteLine("========================================");
            Console.WriteLine("    Dolly Paste UI & Layout Tests       ");
            Console.WriteLine("========================================");

            string tempDir = Path.Combine(Path.GetTempPath(), "DollyPaste_UiTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            int failureCount = 0;

            try
            {
                Settings settings = new Settings();
                settings.Normalize();

                using (HistoryStore history = new HistoryStore(settings, tempDir))
                {
                    // Populate initial clips
                    history.Capture("Shopping list item: organic oats", 1);
                    history.Capture("https://example.com/notes", 2);
                    history.Capture("function add(a, b) { return a + b; }", 3);
                    history.Capture("TemporarySecretKey_XYZ123", 4);
                    history.Protect(4);

                    using (MainForm form = new MainForm(history, null, tempDir, true, false, null))
                    {
                        form.ShowInTaskbar = false;
                        form.StartPosition = FormStartPosition.Manual;
                        form.Location = new Point(-20000, -20000);
                        form.Show();
                        Application.DoEvents();
                        ProcessEvents();

                        // 0. Initial Form and Dialog Outer Size Clamping Regression
                        RunTest("Initial Window Outer Size Clamping (MainForm and SettingsDialog)", delegate
                        {
                            Screen mainScreen = Screen.PrimaryScreen;
                            try
                            {
                                mainScreen = Screen.FromControl(form) ?? Screen.PrimaryScreen;
                            }
                            catch
                            {
                            }
                            Rectangle mainWorkArea = (mainScreen != null) ? mainScreen.WorkingArea : Screen.PrimaryScreen.WorkingArea;

                            Size expectedMainInit, expectedMainMin;
                            MainForm.ClampWindowSize(mainWorkArea, new Size(480, 520), new Size(420, 460), out expectedMainInit, out expectedMainMin);

                            AssertEqual(expectedMainInit.Width, form.Size.Width,
                                "MainForm actual outer width must match clamped initial width (not inflated by client chrome)");
                            AssertEqual(expectedMainInit.Height, form.Size.Height,
                                "MainForm actual outer height must match clamped initial height (not inflated by client chrome)");
                            AssertTrue(form.Size.Width <= mainWorkArea.Width,
                                "MainForm outer width must not exceed working area width");
                            AssertTrue(form.Size.Height <= mainWorkArea.Height,
                                "MainForm outer height must not exceed working area height");
                            AssertEqual(expectedMainMin.Width, form.MinimumSize.Width,
                                "MainForm MinimumSize width must match clamped min width");
                            AssertEqual(expectedMainMin.Height, form.MinimumSize.Height,
                                "MainForm MinimumSize height must match clamped min height");

                            using (SettingsDialog dlg = new SettingsDialog(settings, history, tempDir, true))
                            {
                                dlg.ShowInTaskbar = false;
                                dlg.StartPosition = FormStartPosition.Manual;
                                dlg.Location = new Point(-20000, -20000);
                                dlg.Show();
                                Application.DoEvents();
                                ProcessEvents();

                                Screen dlgScreen = Screen.PrimaryScreen;
                                try
                                {
                                    dlgScreen = Screen.FromControl(dlg) ?? Screen.PrimaryScreen;
                                }
                                catch
                                {
                                }
                                Rectangle dlgWorkArea = (dlgScreen != null) ? dlgScreen.WorkingArea : Screen.PrimaryScreen.WorkingArea;

                                Size expectedDlgInit, expectedDlgMin;
                                MainForm.ClampWindowSize(dlgWorkArea, new Size(560, 620), new Size(520, 520), out expectedDlgInit, out expectedDlgMin);

                                AssertEqual(expectedDlgInit.Width, dlg.Size.Width,
                                    "SettingsDialog actual outer width must match clamped initial width (not inflated by client chrome)");
                                AssertEqual(expectedDlgInit.Height, dlg.Size.Height,
                                    "SettingsDialog actual outer height must match clamped initial height (not inflated by client chrome)");
                                AssertTrue(dlg.Size.Width <= dlgWorkArea.Width,
                                    "SettingsDialog outer width must not exceed working area width");
                                AssertTrue(dlg.Size.Height <= dlgWorkArea.Height,
                                    "SettingsDialog outer height must not exceed working area height");
                                AssertEqual(expectedDlgMin.Width, dlg.MinimumSize.Width,
                                    "SettingsDialog MinimumSize width must match clamped min width");
                                AssertEqual(expectedDlgMin.Height, dlg.MinimumSize.Height,
                                    "SettingsDialog MinimumSize height must match clamped min height");
                            }
                        }, ref failureCount);

                        RunTest("Blueprint Title Bar and Bottom-Right Window Controls", delegate
                        {
                            AssertTrue(form.FormBorderStyle == FormBorderStyle.None,
                                "The sheep title bar must replace the duplicated native window caption");
                            AssertTrue(form.HeaderPanel.BackColor.ToArgb() == Color.FromArgb(1, 82, 154).ToArgb(),
                                "The title bar must use the SSS Blueprint blue");
                            AssertTrue(form.HeaderTitleLabel.ForeColor.ToArgb() == Color.White.ToArgb(),
                                "The title must remain readable on the blue bar");
                            AssertEqual(1, CountVisibleTitleLabels(form), "Dolly Paste must have one visible title");
                            AssertNoBrandTagline(form);
                            AssertTrue(form.HeaderPanel.ClientRectangle.Contains(form.HeaderSheepBounds),
                                "The sheep must fit within the title bar");
                            AssertTrue(form.HeaderSheepBounds.Width >= 24 && form.HeaderSheepBounds.Height >= 24,
                                "The title-bar sheep must remain recognizable");
                            AssertTrue(form.HeaderSheepBounds.Right < form.HeaderTitleLabel.Left,
                                "The sheep must precede the title without touching it");
                            AssertTrue(form.CloseButton.Parent == form.HeaderPanel,
                                "The close button must share the title bar with the sheep");
                            AssertTrue(form.CloseButton.Width >= 30 && form.CloseButton.Height >= 30,
                                "The close button must have a usable click target");
                            Rectangle closeInWindow = form.RectangleToClient(form.CloseButton.RectangleToScreen(form.CloseButton.ClientRectangle));
                            AssertTrue(closeInWindow.Top >= 8 &&
                                       form.HeaderPanel.ClientSize.Height - form.CloseButton.Bottom >= 4 &&
                                       form.ClientSize.Width - closeInWindow.Right >= 8,
                                "The close button must have padding above, below and at the window edge");
                            AssertTrue(form.PauseResumeButton.Parent == form.StatusBarPanel &&
                                       form.SettingsButton.Parent == form.StatusBarPanel,
                                "Pause and settings must live in the bottom bar");
                            AssertTrue(form.PauseResumeButton.Left > form.StatusBarPanel.ClientSize.Width / 2 &&
                                       form.SettingsButton.Left > form.PauseResumeButton.Right,
                                "Pause and settings must occupy the bottom right in that order");
                            VerifyMainFormLayout(form);
                            form.CloseButton.PerformClick();
                            ProcessEvents();
                            AssertTrue(!form.Visible && !form.IsDisposed,
                                "The padded close action must hide the popup without shutting down clipboard history");
                            form.Show();
                            ProcessEvents();
                        }, ref failureCount);

                        RunTest("Borderless Window Retains Edge and Corner Resizing", delegate
                        {
                            Size[] sizes = new Size[] { new Size(480, 520), new Size(420, 460) };
                            foreach (Size size in sizes)
                            {
                                AssertEqual(13, MainForm.GetFrameHitTest(new Point(0, 0), size), "Top-left corner must resize both edges");
                                AssertEqual(14, MainForm.GetFrameHitTest(new Point(size.Width - 1, 0), size), "Top-right corner must resize both edges");
                                AssertEqual(16, MainForm.GetFrameHitTest(new Point(0, size.Height - 1), size), "Bottom-left corner must resize both edges");
                                AssertEqual(17, MainForm.GetFrameHitTest(new Point(size.Width - 1, size.Height - 1), size), "Bottom-right corner must resize both edges");
                                AssertEqual(10, MainForm.GetFrameHitTest(new Point(0, size.Height / 2), size), "Left edge must resize horizontally");
                                AssertEqual(11, MainForm.GetFrameHitTest(new Point(size.Width - 1, size.Height / 2), size), "Right edge must resize horizontally");
                                AssertEqual(12, MainForm.GetFrameHitTest(new Point(size.Width / 2, 0), size), "Top edge must resize vertically");
                                AssertEqual(15, MainForm.GetFrameHitTest(new Point(size.Width / 2, size.Height - 1), size), "Bottom edge must resize vertically");
                                AssertEqual(1, MainForm.GetFrameHitTest(new Point(5, 5), size), "Resize perimeter must not cover the inset app controls");
                                AssertEqual(1, MainForm.GetFrameHitTest(new Point(size.Width / 2, size.Height / 2), size), "Content must remain an ordinary client area");
                            }
                        }, ref failureCount);

                        // 1. Search and Filter Tests
                        RunTest("Search Filter by Text Query", delegate
                        {
                            form.TestApplySearch("Shopping");
                            ProcessEvents();
                            AssertEqual(1, form.ClipsListBox.Items.Count, "Search for 'Shopping' should return exactly 1 clip");

                            form.TestApplySearch("");
                            ProcessEvents();
                            AssertEqual(4, form.ClipsListBox.Items.Count, "Clearing search should restore 4 clips");
                        }, ref failureCount);

                        RunTest("Filter Pills by Kind", delegate
                        {
                            form.TestSetFilterKind("CODE");
                            ProcessEvents();
                            AssertEqual(1, form.ClipsListBox.Items.Count, "Filter by CODE should return 1 clip");

                            form.TestSetFilterKind("LINK");
                            ProcessEvents();
                            AssertEqual(1, form.ClipsListBox.Items.Count, "Filter by LINK should return 1 clip");

                            form.TestSetFilterKind("TEXT");
                            ProcessEvents();
                            AssertEqual(2, form.ClipsListBox.Items.Count, "Filter by TEXT should return 2 clips");

                            form.TestSetFilterKind(null);
                            ProcessEvents();
                            AssertEqual(4, form.ClipsListBox.Items.Count, "Filter by All should return 4 clips");
                        }, ref failureCount);

                        // 2. Selection and Pinning Tests
                        RunTest("Selection and Pin Toggle", delegate
                        {
                            form.TestSetFilterKind(null);
                            form.TestApplySearch("");
                            ProcessEvents();

                            form.TestSelectClip(0);
                            ProcessEvents();

                            bool initialPin = form.PinButton.Text.IndexOf("★") >= 0;
                            form.TestTogglePinSelected();
                            ProcessEvents();
                            bool toggledPin = form.PinButton.Text.IndexOf("★") >= 0;
                            AssertTrue(initialPin != toggledPin, "Pin state should invert after toggle");

                            // Revert pin state
                            form.TestTogglePinSelected();
                            ProcessEvents();
                            AssertTrue((form.PinButton.Text.IndexOf("★") >= 0) == initialPin, "Pin state should revert after second toggle");
                        }, ref failureCount);

                        // 3. Automatic Changed Event Path (No TestRefresh required)
                        RunTest("Automatic Changed Event Path (Capture, Protect, Preview, Censor)", delegate
                        {
                            string secretPayload = "SuperSecretToken_AlphaBetaGamma_999";
                            history.Capture(secretPayload, 10);
                            ProcessEvents();

                            // Select newly captured entry without calling TestRefresh()
                            form.TestSelectClip(0);
                            ProcessEvents();

                            AssertTrue(form.PreviewTextBox.Text.IndexOf(secretPayload) >= 0,
                                "Initial preview should show captured payload automatically via HistoryStore Changed event without TestRefresh()");
                            AssertTrue(form.CopyButton.Enabled, "Copy button should be enabled for plain text clip");

                            // Now protect sequence 10
                            history.Protect(10);
                            ProcessEvents();

                            AssertTrue(form.PreviewTextBox.Text.IndexOf(secretPayload) < 0,
                                "Protected preview must NOT contain original secret payload");
                            AssertTrue(form.PreviewTextBox.Text.IndexOf("Protected content removed") >= 0,
                                "Preview should state that protected content was removed");
                            AssertTrue(!form.CopyButton.Enabled, "Copy button must be disabled for protected clip");

                            // Verify no hidden control or list entry retains secret payload
                            AssertTrue(form.SearchTextBox.Text.IndexOf(secretPayload) < 0,
                                "Search control must not retain secret");

                            for (int i = 0; i < form.ClipsListBox.Items.Count; i++)
                            {
                                ClipEntry clip = form.ClipsListBox.Items[i] as ClipEntry;
                                if (clip != null && clip.IsSensitive)
                                {
                                    AssertTrue(clip.Text == null || clip.Text.IndexOf(secretPayload) < 0,
                                        "ClipEntry in list box must not contain secret payload");
                                }
                            }
                        }, ref failureCount);

                        // 4. Compact layout and optional preview at the default size.
                        RunTest("Compact Layout and Optional Preview at Default Size (480x520)", delegate
                        {
                            form.Size = new Size(480, 520);
                            form.TestSelectClip(0);
                            form.PerformLayout();
                            ProcessEvents();

                            AssertTrue(!form.DetailPanel.Visible, "Preview should be collapsed initially");
                            VerifyMainFormLayout(form);
                            int collapsedListHeight = form.ListContainerPanel.Height;
                            form.PreviewToggleButton.PerformClick();
                            ProcessEvents();
                            AssertTrue(form.DetailPanel.Visible, "Preview toggle should expose the selected clip");
                            AssertTrue(form.ListContainerPanel.Height < collapsedListHeight,
                                "Expanding preview should share the popup with the list");
                            VerifyMainFormLayout(form);
                            form.PreviewToggleButton.PerformClick();
                            ProcessEvents();
                            AssertTrue(!form.DetailPanel.Visible, "Preview toggle should collapse the preview again");
                            AssertEqual(collapsedListHeight, form.ListContainerPanel.Height,
                                "Collapsing preview should restore the list's available space");
                        }, ref failureCount);

                        // 5. Minimum size must preserve usable list space even with the preview open.
                        RunTest("Compact Layout and Optional Preview at Minimum Size (420x460)", delegate
                        {
                            if (form.DetailPanel.Visible) form.PreviewToggleButton.PerformClick();
                            form.Size = new Size(420, 460);
                            form.TestSelectClip(0);
                            form.PerformLayout();
                            ProcessEvents();

                            VerifyMainFormLayout(form);
                            form.PreviewToggleButton.PerformClick();
                            ProcessEvents();
                            AssertTrue(form.DetailPanel.Visible, "Preview must open at minimum size");
                            AssertTrue(form.ListContainerPanel.Height >= 100,
                                "Expanded preview must leave at least 100 pixels for clipboard items");
                            AssertTrue(form.DetailPanel.Height <= 168,
                                "Expanded preview must remain compact");
                            VerifyMainFormLayout(form);
                            form.PreviewToggleButton.PerformClick();
                        }, ref failureCount);

                        // 6. Windows DPI Virtualization & AutoScaleMode Policy
                        RunTest("Windows DPI Virtualization & AutoScaleMode Policy", delegate
                        {
                            // 1. MainForm has AutoScaleMode.None
                            AssertTrue(form.AutoScaleMode == AutoScaleMode.None, "MainForm AutoScaleMode must be None");

                            // 2. Graphics DpiX is ~96 logical DPI under DPI virtualization
                            using (Graphics g = form.CreateGraphics())
                            {
                                AssertTrue(Math.Abs(g.DpiX - 96f) < 2.0f,
                                    string.Format("Graphics DpiX should be ~96 under Windows DPI virtualization (actual: {0})", g.DpiX));
                            }

                            // 3. SettingsDialog has AutoScaleMode.None
                            using (SettingsDialog dlg = new SettingsDialog(settings, history, tempDir, true))
                            {
                                AssertTrue(dlg.AutoScaleMode == AutoScaleMode.None, "SettingsDialog AutoScaleMode must be None");
                            }

                            // 4. Verify app.manifest policy: dpiAware=false and dpiAwareness=unaware
                            string manifestPath = "app.manifest";
                            if (!File.Exists(manifestPath))
                            {
                                string candidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.manifest");
                                if (File.Exists(candidate)) manifestPath = candidate;
                                else
                                {
                                    candidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "app.manifest");
                                    if (File.Exists(candidate)) manifestPath = candidate;
                                }
                            }

                            if (File.Exists(manifestPath))
                            {
                                string manifestText = File.ReadAllText(manifestPath);
                                AssertTrue(manifestText.IndexOf("<dpiAware", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                           manifestText.IndexOf("false", StringComparison.OrdinalIgnoreCase) >= 0,
                                    "app.manifest must specify dpiAware=false");
                                AssertTrue(manifestText.IndexOf("<dpiAwareness", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                           manifestText.IndexOf("unaware", StringComparison.OrdinalIgnoreCase) >= 0,
                                    "app.manifest must specify dpiAwareness=unaware");
                                AssertTrue(manifestText.IndexOf("PerMonitorV2", StringComparison.OrdinalIgnoreCase) < 0,
                                    "app.manifest must not specify PerMonitorV2");
                                AssertTrue(manifestText.IndexOf("true/pm", StringComparison.OrdinalIgnoreCase) < 0,
                                    "app.manifest must not specify true/pm");
                            }
                        }, ref failureCount);

                        // 6b. Pure Bounded Size Helper & Screen Fit Clamping
                        RunTest("Pure Bounded Size Helper & Screen Fit Clamping", delegate
                        {
                            // 1. Large working area preserves compact default and minimum sizes.
                            Rectangle largeArea = new Rectangle(0, 0, 1920, 1080);
                            Size initLarge, minLarge;
                            MainForm.ClampWindowSize(largeArea, new Size(420, 520), new Size(380, 420), out initLarge, out minLarge);
                            AssertEqual(420, initLarge.Width, "Large desktop should preserve default 420 width");
                            AssertEqual(520, initLarge.Height, "Large desktop should preserve default 520 height");
                            AssertEqual(380, minLarge.Width, "Large desktop should preserve min 380 width");
                            AssertEqual(420, minLarge.Height, "Large desktop should preserve min 420 height");

                            // 2. Small working height clamps both initial and minimum height.
                            Rectangle smallArea = new Rectangle(0, 0, 1024, 390);
                            Size initSmall, minSmall;
                            MainForm.ClampWindowSize(smallArea, new Size(420, 520), new Size(380, 420), out initSmall, out minSmall);
                            AssertTrue(initSmall.Height <= smallArea.Height, "Initial height must fit within working area height");
                            AssertTrue(minSmall.Height < 420, "Minimum height must clamp below 420 when working height is smaller");
                            AssertTrue(minSmall.Height <= initSmall.Height, "Minimum height must be <= initial height");

                            Size exactAreaInitial, exactAreaMinimum;
                            MainForm.ClampWindowSize(new Rectangle(0, 0, 380, 420), new Size(420, 520),
                                new Size(380, 420), out exactAreaInitial, out exactAreaMinimum);
                            AssertEqual(364, exactAreaInitial.Width, "Screen fit must reserve horizontal window padding");
                            AssertEqual(388, exactAreaInitial.Height, "Screen fit must reserve vertical window padding");
                            AssertTrue(exactAreaMinimum.Width <= exactAreaInitial.Width &&
                                       exactAreaMinimum.Height <= exactAreaInitial.Height,
                                "A working area matching requested minimum must not grow the minimum beyond the padded initial size");

                            Size reducedInitial, reducedMinimum;
                            MainForm.ClampWindowSize(largeArea, new Size(300, 350), new Size(380, 420),
                                out reducedInitial, out reducedMinimum);
                            AssertEqual(300, reducedInitial.Width, "A smaller requested window must retain its width");
                            AssertEqual(350, reducedInitial.Height, "A smaller requested window must retain its height");
                            AssertTrue(reducedMinimum.Width <= reducedInitial.Width && reducedMinimum.Height <= reducedInitial.Height,
                                "Requested minimum must clamp to a smaller initial window even on a large display");

                            // 3. SettingsDialog clamping on small working area
                            Size initSettings, minSettings;
                            MainForm.ClampWindowSize(smallArea, new Size(560, 620), new Size(520, 520), out initSettings, out minSettings);
                            AssertTrue(initSettings.Height <= smallArea.Height, "Settings initial height must fit within working area");
                            AssertTrue(minSettings.Height <= smallArea.Height, "Settings min height must fit within working area");

                            // 4. Smaller owned window state maintains positive content regions
                            form.Size = initSmall;
                            form.PerformLayout();
                            ProcessEvents();
                            AssertTrue(form.ContentPanel.Height > 0, "ContentPanel height must remain positive");
                            AssertTrue(form.ListContainerPanel.Width > 0, "ListContainer width must remain positive");
                            AssertTrue(form.DetailToolbarPanel.Visible, "Clip actions must remain available without a preview");

                            // Restore normal size for remaining tests
                            form.Size = new Size(480, 520);
                            form.PerformLayout();
                            ProcessEvents();
                        }, ref failureCount);

                        RunTest("Popup Placement Within Working Areas", delegate
                        {
                            Rectangle[] workingAreas = new Rectangle[]
                            {
                                new Rectangle(0, 0, 1920, 1040),
                                new Rectangle(-1920, -200, 1920, 1040),
                                new Rectangle(100, 80, 300, 250),
                                new Rectangle(-20, -10, 12, 10)
                            };
                            foreach (Rectangle area in workingAreas)
                            {
                                Point[] anchors = new Point[]
                                {
                                    area.Location,
                                    new Point(area.Right - 1, area.Bottom - 1),
                                    new Point(area.Left + area.Width / 2, area.Top + area.Height / 2),
                                    new Point(area.Left - 1000, area.Top - 1000),
                                    new Point(area.Right + 1000, area.Bottom + 1000)
                                };
                                foreach (Point anchor in anchors)
                                {
                                    Rectangle bounds = MainForm.GetPopupBounds(area, anchor, new Size(420, 520));
                                    AssertTrue(bounds.Width > 0 && bounds.Height > 0,
                                        "Popup bounds must remain positive even on a tiny working area");
                                    AssertTrue(area.Contains(bounds),
                                        string.Format("Popup {0} must fit working area {1} for anchor {2}", bounds, area, anchor));
                                    if (area.Width > 16 && area.Height > 16)
                                    {
                                        Rectangle inset = Rectangle.Inflate(area, -8, -8);
                                        AssertTrue(inset.Contains(bounds), "Popup must preserve an 8-pixel working-area margin");
                                    }
                                    if (area.Width >= 436 && area.Height >= 536)
                                    {
                                        AssertEqual(420, bounds.Width, "Roomy displays must preserve popup width");
                                        AssertEqual(520, bounds.Height, "Roomy displays must preserve popup height");
                                    }
                                }
                            }
                        }, ref failureCount);

                        RunTest("Search Keyboard Navigation and Guarded Actions", delegate
                        {
                            if (form.DetailPanel.Visible) form.PreviewToggleButton.PerformClick();
                            form.TestSetFilterKind(null);
                            form.TestApplySearch("");
                            form.TestSelectClip(0);
                            ProcessEvents();
                            AssertTrue(form.SearchTextBox.Focus(), "Search must receive focus");
                            AssertTrue(PressKey(form, Keys.Down), "Down from search must be handled");
                            AssertEqual(1, form.ClipsListBox.SelectedIndex, "Down must advance the selected clip");
                            AssertTrue(form.SearchTextBox.Focused, "Arrow navigation must keep typing focus in search");
                            AssertTrue(PressKey(form, Keys.Up), "Up from search must be handled");
                            AssertEqual(0, form.ClipsListBox.SelectedIndex, "Up must select the preceding clip");
                            PressKey(form, Keys.Up);
                            AssertEqual(0, form.ClipsListBox.SelectedIndex, "Up must clamp at the first clip");
                            form.TestSelectClip(form.ClipsListBox.Items.Count - 1);
                            PressKey(form, Keys.Down);
                            AssertEqual(form.ClipsListBox.Items.Count - 1, form.ClipsListBox.SelectedIndex,
                                "Down must clamp at the last clip");
                            AssertTrue(!PressKey(form, Keys.Control | Keys.Down), "Modified arrows must retain text-box behavior");

                            form.TestApplySearch("Shopping");
                            Label toast = GetToast(form);
                            toast.Text = "";
                            AssertTrue(PressKey(form, Keys.Enter), "Enter from search must copy the selected match");
                            AssertTrue(toast.Text.Contains("copy simulated"), "Search Enter must use the guarded demo copy action");
                            AssertTrue(form.Visible, "Simulated copy must keep the demo popup visible");

                            Guid selectedId = ((ClipEntry)form.ClipsListBox.SelectedItem).Id;
                            bool wasPinned = ((ClipEntry)form.ClipsListBox.SelectedItem).IsPinned;
                            AssertTrue(PressKey(form, Keys.Control | Keys.P), "Ctrl+P from search must pin the selected match");
                            AssertTrue(((ClipEntry)form.ClipsListBox.SelectedItem).Id == selectedId,
                                "Pinning must preserve selected clip identity");
                            AssertTrue(((ClipEntry)form.ClipsListBox.SelectedItem).IsPinned != wasPinned,
                                "Ctrl+P must change the selected clip's pin state");
                            PressKey(form, Keys.Control | Keys.P);
                            AssertTrue(((ClipEntry)form.ClipsListBox.SelectedItem).IsPinned == wasPinned,
                                "A second Ctrl+P must restore the original pin state");

                            AssertTrue(!form.DetailPanel.Visible, "Keyboard preview must begin collapsed");
                            AssertTrue(PressKey(form, Keys.Control | Keys.Space), "Ctrl+Space must expand the preview");
                            AssertTrue(form.DetailPanel.Visible, "Keyboard preview toggle must reveal details");
                            PressKey(form, Keys.Control | Keys.Space);
                            AssertTrue(!form.DetailPanel.Visible, "Keyboard preview toggle must collapse details");

                            form.TestApplySearch("protected content removed");
                            toast.Text = "";
                            AssertTrue(PressKey(form, Keys.Enter), "Protected match Enter must be consumed");
                            AssertTrue(toast.Text.Length == 0, "Protected match Enter must not copy anything");
                            form.TestApplySearch("no matching clipboard fixture 84721");
                            AssertEqual(0, form.ClipsListBox.Items.Count, "No-match fixture must empty the list");
                            PressKey(form, Keys.Up);
                            PressKey(form, Keys.Down);
                            AssertEqual(-1, form.ClipsListBox.SelectedIndex, "Empty-list navigation must remain safe");
                            AssertTrue(PressKey(form, Keys.Enter), "Empty-list Enter must be consumed safely");
                            AssertTrue(toast.Text.Length == 0, "Empty-list Enter must not copy anything");
                            AssertTrue(PressKey(form, Keys.Control | Keys.F), "Ctrl+F must focus search");
                            AssertTrue(form.SearchTextBox.Focused, "Ctrl+F must leave search focused");
                            AssertEqual(form.SearchTextBox.Text.Length, form.SearchTextBox.SelectionLength,
                                "Ctrl+F must select the current search query");
                            form.TestApplySearch("");
                        }, ref failureCount);

                        RunTest("Dismiss and Reopen Starts a Fresh Search", delegate
                        {
                            form.TestApplySearch("Shopping");
                            if (!form.DetailPanel.Visible) form.PreviewToggleButton.PerformClick();
                            AssertTrue(form.DetailPanel.Visible, "Reopen fixture must begin with expanded preview");
                            form.NavPinnedButton.PerformClick();
                            form.TestSetFilterKind("CODE");
                            form.TestApplySearch("unmatched previous search");
                            AssertTrue(PressKey(form, Keys.Escape), "Escape must dismiss the popup");
                            AssertTrue(!form.Visible, "Escape must hide the popup");

                            try
                            {
                                form.ShowFromTray();
                                ProcessEvents();
                                AssertTrue(form.Visible, "Tray invocation must restore the popup");
                                AssertTrue(form.SearchTextBox.Text.Length == 0, "Reopen must clear the previous query");
                                AssertEqual(history.Snapshot().Count, form.ClipsListBox.Items.Count,
                                    "Reopen must reset both kind and pinned filters");
                                AssertEqual(0, form.ClipsListBox.SelectedIndex, "Reopen must select the first result");
                                AssertTrue(!form.DetailPanel.Visible, "Reopen must collapse the preview");
                                AssertTrue(form.SearchTextBox.Focused, "Reopen must focus search for immediate typing");
                                Rectangle workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
                                AssertTrue(workingArea.Contains(form.Bounds), "Reopened popup must fit the pointer's monitor");
                            }
                            finally
                            {
                                form.Location = new Point(-20000, -20000);
                            }
                        }, ref failureCount);

                        RunTest("History Ctrl+C Routing and Protected Entry Guards", delegate
                        {
                            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                            var processKey = typeof(MainForm).GetMethod("ProcessCmdKey", flags);
                            Label toast = (Label)typeof(MainForm).GetField("_lblToast", flags).GetValue(form);
                            Func<Keys, bool> pressKey = delegate(Keys keys)
                            {
                                Message message = Message.Create(form.Handle, 0x0100, IntPtr.Zero, IntPtr.Zero);
                                return (bool)processKey.Invoke(form, new object[] { message, keys });
                            };

                            form.TestSetFilterKind(null);
                            form.TestApplySearch("Shopping");
                            form.TestSelectClip(0);
                            ProcessEvents();
                            AssertTrue(form.ClipsListBox.Focus(), "History list must receive focus");
                            toast.Text = "";
                            AssertTrue(pressKey(Keys.Control | Keys.C), "History Ctrl+C must be handled");
                            AssertTrue(toast.Text.Contains("copy simulated"), "History Ctrl+C must reach the guarded copy action");

                            toast.Text = "";
                            AssertTrue(pressKey(Keys.Enter), "Enter must still copy the selected entry");
                            AssertTrue(toast.Text.Contains("copy simulated"), "Enter must reach the same copy action");
                            toast.Text = "";
                            AssertTrue(!pressKey(Keys.Control | Keys.Shift | Keys.C), "Ctrl+Shift+C must not be captured");
                            AssertTrue(toast.Text.Length == 0, "Modified shortcut must not copy an entry");

                            AssertTrue(form.SearchTextBox.Focus(), "Search must receive focus");
                            form.SearchTextBox.SelectAll();
                            AssertTrue(!pressKey(Keys.Control | Keys.C), "Form must leave selected search text Ctrl+C to the text box");
                            AssertTrue(toast.Text.Length == 0, "Copying selected search text must not copy a history entry");
                            form.SearchTextBox.Select(form.SearchTextBox.Text.Length, 0);
                            AssertTrue(pressKey(Keys.Control | Keys.C), "Form must route search Ctrl+C to the clip when no query text is selected");
                            AssertTrue(toast.Text.Contains("copy simulated"), "Unselected search Ctrl+C must reach the guarded clip copy action");
                            toast.Text = "";

                            form.TestApplySearch("");
                            int protectedIndex = -1;
                            for (int i = 0; i < form.ClipsListBox.Items.Count; i++)
                            {
                                ClipEntry clip = form.ClipsListBox.Items[i] as ClipEntry;
                                if (clip != null && clip.IsSensitive) { protectedIndex = i; break; }
                            }
                            AssertTrue(protectedIndex >= 0, "Fixture must contain a protected entry");
                            form.TestSelectClip(protectedIndex);
                            AssertTrue(form.ClipsListBox.Focus(), "History list must regain focus");
                            AssertTrue(pressKey(Keys.Control | Keys.C), "Protected entry shortcut must be consumed");
                            AssertTrue(toast.Text.Length == 0, "Protected entry must not reach clipboard copy");

                            form.ClipsListBox.SelectedIndex = -1;
                            AssertTrue(pressKey(Keys.Control | Keys.C), "Shortcut with no selection must be consumed safely");
                            AssertTrue(toast.Text.Length == 0, "No selection must not trigger clipboard copy");
                        }, ref failureCount);

                        RunTest("Focused Search Ctrl+C Copies the Selected Clip After Reopening", delegate
                        {
                            Label toast = GetToast(form);
                            try
                            {
                                // ShowFromTray is the popup-opening path used by the global shortcut.
                                form.ShowFromTray();
                                ProcessEvents();
                                AssertTrue(form.SearchTextBox.Focused, "Reopening must leave search focused");
                                AssertEqual(0, form.SearchTextBox.Text.Length, "Reopening must start with an empty query");
                                int ordinaryIndex = -1;
                                for (int i = 0; i < form.ClipsListBox.Items.Count; i++)
                                {
                                    ClipEntry clip = form.ClipsListBox.Items[i] as ClipEntry;
                                    if (clip != null && !clip.IsSensitive && clip.Text.IndexOf("Shopping", StringComparison.Ordinal) >= 0)
                                    {
                                        ordinaryIndex = i;
                                        break;
                                    }
                                }
                                AssertTrue(ordinaryIndex >= 0, "Fixture must contain an ordinary clip to copy");
                                form.TestSelectClip(ordinaryIndex);
                                AssertTrue(form.SearchTextBox.Focused, "Choosing a clip must retain search focus");
                                toast.Text = "";
                                AssertTrue(PressTextBoxKey(form.SearchTextBox, Keys.Control | Keys.C),
                                    "The focused search control must consume Ctrl+C with an empty query");
                                AssertTrue(toast.Text.Contains("copy simulated"),
                                    "Ctrl+C from the actual search control must copy the selected clip");

                                form.TestApplySearch("Shopping");
                                form.SearchTextBox.Select(form.SearchTextBox.Text.Length, 0);
                                toast.Text = "";
                                AssertTrue(PressTextBoxKey(form.SearchTextBox, Keys.Control | Keys.C),
                                    "A typed query with only a caret must still allow copying the selected clip");
                                AssertTrue(toast.Text.Contains("copy simulated"),
                                    "An unselected query must route Ctrl+C to the matching clip");

                                form.SearchTextBox.SelectAll();
                                toast.Text = "";
                                AssertTrue(PressTextBoxKey(form.SearchTextBox, Keys.Control | Keys.C),
                                    "Demo mode must safely consume copying selected query text");
                                AssertTrue(toast.Text.Length == 0,
                                    "Selected query text must take precedence over copying a history clip");
                                SendTextBoxCopyMessage(form.SearchTextBox);
                                AssertTrue(toast.Text.Length == 0,
                                    "Native copy messages with selected query text must not copy a history clip");

                                form.SearchTextBox.Select(form.SearchTextBox.Text.Length, 0);
                                SendTextBoxCopyMessage(form.SearchTextBox);
                                AssertTrue(toast.Text.Contains("copy simulated"),
                                    "Native copy messages without selected query text must route through guarded clip copying");
                                toast.Text = "";
                                // WinForms may consume this shortcut when native clipboard shortcuts are disabled;
                                // the application must still leave it out of its clip-copy routing.
                                PressTextBoxKey(form.SearchTextBox, Keys.Control | Keys.Shift | Keys.C);
                                AssertTrue(toast.Text.Length == 0, "Modified shortcuts must not copy a clip");

                                form.TestApplySearch("protected content removed");
                                form.SearchTextBox.Select(form.SearchTextBox.Text.Length, 0);
                                AssertTrue(PressTextBoxKey(form.SearchTextBox, Keys.Control | Keys.C),
                                    "Ctrl+C with a legacy protected match must be consumed safely");
                                SendTextBoxCopyMessage(form.SearchTextBox);
                                AssertTrue(toast.Text.Length == 0, "Legacy protected matches must never be copied from search");

                                form.TestApplySearch("no matching clipboard fixture 84721");
                                form.SearchTextBox.Select(form.SearchTextBox.Text.Length, 0);
                                AssertEqual(0, form.ClipsListBox.Items.Count, "No-match fixture must empty the history list");
                                AssertTrue(PressTextBoxKey(form.SearchTextBox, Keys.Control | Keys.C),
                                    "Ctrl+C without any matching clip must be consumed safely");
                                SendTextBoxCopyMessage(form.SearchTextBox);
                                AssertTrue(toast.Text.Length == 0, "No-match search must not trigger clipboard copying");
                            }
                            finally
                            {
                                form.TestApplySearch("");
                                form.Location = new Point(-20000, -20000);
                            }
                        }, ref failureCount);

                        RunTest("Normal Search TextBox Routes Copy Without Selected Query Text", delegate
                        {
                            int routedCopies = 0;
                            using (PrivacyTextBox search = new PrivacyTextBox(false, delegate { routedCopies++; }, true))
                            {
                                search.Text = "query";
                                search.Select(search.Text.Length, 0);
                                AssertTrue(PressTextBoxKey(search, Keys.Control | Keys.C),
                                    "Normal search must intercept Ctrl+C without selected query text");
                                AssertEqual(1, routedCopies, "Normal search Ctrl+C must invoke its clip-copy callback exactly once");
                                SendTextBoxCopyMessage(search);
                                AssertEqual(2, routedCopies, "Normal search WM_COPY must invoke the same clip-copy callback exactly once");
                            }
                        }, ref failureCount);

                        // 7. Privacy TextBox Behavior Seam
                        RunTest("Privacy TextBox Clipboard Suppression", delegate
                        {
                            AssertTrue(!form.SearchTextBox.ShortcutsEnabled,
                                "SearchTextBox shortcuts must be disabled in demo mode");
                            AssertTrue(form.SearchTextBox.ContextMenu != null && form.SearchTextBox.ContextMenu.MenuItems.Count == 0,
                                "SearchTextBox context menu must be empty dummy menu");

                            AssertTrue(!form.PreviewTextBox.ShortcutsEnabled,
                                "PreviewTextBox shortcuts must be disabled in demo mode");
                            AssertTrue(form.PreviewTextBox.ContextMenu != null && form.PreviewTextBox.ContextMenu.MenuItems.Count == 0,
                                "PreviewTextBox context menu must be empty dummy menu");

                            using (PrivacyTextBox customBox = new PrivacyTextBox(true, null))
                            {
                                AssertTrue(!customBox.ShortcutsEnabled, "PrivacyTextBox shortcuts must be disabled");
                                AssertTrue(customBox.ContextMenu != null && customBox.ContextMenu.MenuItems.Count == 0,
                                    "PrivacyTextBox context menu must be empty dummy menu");
                            }
                        }, ref failureCount);

                        RunTest("Demo Popup Remains Visible on Deactivation", delegate
                        {
                            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                            var deactivate = typeof(MainForm).GetMethod("OnDeactivate", flags);
                            deactivate.Invoke(form, new object[] { EventArgs.Empty });
                            AssertTrue(form.Visible, "Demo popup must stay visible when focus changes");
                        }, ref failureCount);
                    }

                    RunTest("Image Base64 Preview, Filtering, Layout and Disposal", delegate
                    {
                        string imageDirectory = Path.Combine(tempDir, "ImageUi_" + Guid.NewGuid().ToString("N"));
                        string imageBase64 = CreateSyntheticImageBase64();
                        AssertTrue(imageBase64.Length > 32768, "Fixture must exceed the native text edit default limit");
                        using (HistoryStore imageHistory = new HistoryStore(settings, imageDirectory))
                        {
                            imageHistory.Capture("Ordinary text beside image history", 8001);
                            ClipEntry imageEntry = imageHistory.CaptureImage(imageBase64, 8002);
                            using (MainForm imageForm = new MainForm(imageHistory, null, imageDirectory, true, false, null))
                            {
                                imageForm.ShowInTaskbar = false;
                                imageForm.StartPosition = FormStartPosition.Manual;
                                imageForm.Location = new Point(-20000, -20000);
                                imageForm.Show();
                                ProcessEvents();
                                imageForm.FilterImageButton.PerformClick();
                                AssertEqual(1, imageForm.ClipsListBox.Items.Count, "Images filter must isolate image clips");
                                AssertTrue(imageForm.DetailKindLabel.Text == "IMAGE", "Image entry must have an IMAGE label");
                                AssertTrue(imageForm.PreviewTextBox.Text == imageBase64, "Left preview must retain the entire raw PNG base64 value");
                                AssertTrue(imageForm.OcrPreviewTextBox.Text.Contains("Reading text"), "Right preview must initially explain that text recognition is pending");
                                AssertTrue(!imageForm.CopyImageTextButton.Enabled, "Text copy must be unavailable before OCR finishes");
                                AssertTrue(imageForm.ImagePreviewBox.Image != null, "Image entry must decode a visual preview");
                                AssertTrue(imageForm.ImagePreviewBox.Image.Width <= 240 && imageForm.ImagePreviewBox.Image.Height <= 240,
                                    "Image preview must retain only a bounded thumbnail");

                                imageForm.PreviewToggleButton.PerformClick();
                                ProcessEvents();
                                AssertTrue(imageForm.PreviewTextBox.Text == imageBase64, "Base64 preview must retain the entire raw PNG value");
                                AssertTrue(!imageForm.PreviewTextBox.WordWrap && imageForm.PreviewTextBox.ScrollBars == ScrollBars.Both,
                                    "Base64 must scroll without expensive wrapping of a large unbroken value");
                                AssertTrue(imageForm.OcrPreviewTextBox.WordWrap, "Recognized text must retain normal readable wrapping");
                                AssertTrue(imageForm.ImagePreviewBox.Visible, "Expanded preview must show the image");
                                AssertTrue(imageForm.PreviewTextBox.Left >= imageForm.ImagePreviewBox.Parent.Right,
                                    "Base64 value must appear beside the image");
                                VerifyMainFormLayout(imageForm);
                                imageForm.Size = new Size(420, 460);
                                ProcessEvents();
                                VerifyMainFormLayout(imageForm);
                                AssertTrue(imageForm.PreviewTextBox.Width > 100, "Base64 value must remain readable at minimum size");
                                AssertTrue(imageForm.ImagePreviewBox.Height >= 64,
                                    "Image preview must remain visible at minimum size (actual height " + imageForm.ImagePreviewBox.Height + ")");
                                AssertTrue(imageForm.ListContainerPanel.Height >= imageForm.ClipsListBox.ItemHeight,
                                    "Expanded image preview must preserve at least one full clipboard row");

                                using (Bitmap rendered = new Bitmap(imageForm.Width, imageForm.Height))
                                    imageForm.DrawToBitmap(rendered, new Rectangle(Point.Empty, rendered.Size));
                                AssertEqual(1, imageForm.CachedImageThumbnailCount, "Painting must cache the image thumbnail");
                                imageForm.CopyButton.PerformClick();
                                AssertTrue(GetToast(imageForm).Text.Contains("image copy simulated"), "An image entry must copy the image by default through the guarded demo route");
                                AssertTrue(imageHistory.GetText(imageEntry.Id) == imageBase64, "Copy must preserve the full base64 payload");

                                Image oldPreview = imageForm.ImagePreviewBox.Image;
                                imageForm.FilterTextButton.PerformClick();
                                AssertEqual(1, imageForm.ClipsListBox.Items.Count, "Text filter must exclude images");
                                AssertTrue(imageForm.ImagePreviewBox.Image == null, "Selecting text must clear the image preview");
                                AssertTrue(imageForm.PreviewTextBox.WordWrap && imageForm.PreviewTextBox.ScrollBars == ScrollBars.Vertical,
                                    "Selecting ordinary text must restore wrapped text preview");
                                AssertImageDisposed(oldPreview, "Selecting text must dispose the previous image preview");
                                imageForm.FilterImageButton.PerformClick();
                                oldPreview = imageForm.ImagePreviewBox.Image;
                                imageHistory.Remove(imageEntry.Id);
                                ProcessEvents();
                                AssertEqual(0, imageForm.ClipsListBox.Items.Count, "Removed image must leave the image filter empty");
                                AssertEqual(0, imageForm.CachedImageThumbnailCount, "History refresh must release stale thumbnail cache entries");
                                AssertTrue(imageForm.ImagePreviewBox.Image == null, "Removing an image must clear its visual preview");
                                AssertImageDisposed(oldPreview, "Removing an image must dispose its visual preview");

                                imageHistory.CaptureImage(imageBase64, 8003);
                                ProcessEvents();
                                oldPreview = imageForm.ImagePreviewBox.Image;
                                imageForm.Dispose();
                                AssertEqual(0, imageForm.CachedImageThumbnailCount, "Disposing the form must release cached thumbnails");
                                AssertImageDisposed(oldPreview, "Disposing the form must release its selected image preview");
                            }
                        }
                    }, ref failureCount);

                    RunTest("Image OCR Completion, Search and Distinct Copy Actions", delegate
                    {
                        string ocrDirectory = Path.Combine(tempDir, "OcrUi_" + Guid.NewGuid().ToString("N"));
                        string imageBase64 = CreateSyntheticImageBase64();
                        string recognizedText = "Synthetic receipt\nTotal: 42.00";
                        using (HistoryStore ocrHistory = new HistoryStore(settings, ocrDirectory))
                        {
                            ClipEntry imageEntry = ocrHistory.CaptureImage(imageBase64, 8101);
                            using (MainForm ocrForm = new MainForm(ocrHistory, null, ocrDirectory, true, false, null))
                            {
                                ocrForm.ShowInTaskbar = false;
                                ocrForm.StartPosition = FormStartPosition.Manual;
                                ocrForm.Location = new Point(-20000, -20000);
                                ocrForm.Show();
                                ProcessEvents();
                                ocrForm.PreviewToggleButton.PerformClick();
                                AssertTrue(ocrForm.OcrPreviewTextBox.Text.Contains("Reading text"), "Pending status must be readable beside base64");
                                AssertTrue(ocrForm.PreviewTextBox.Text == imageBase64, "Pending OCR must preserve the simultaneous base64 preview");
                                AssertTrue(!ocrForm.CopyImageTextButton.Enabled, "Pending OCR must disable Copy text");
                                AssertTrue(ocrForm.CopyButton.Enabled, "Pending OCR must leave image copying available");

                                ocrForm.TestApplySearch("receipt");
                                AssertEqual(0, ocrForm.ClipsListBox.Items.Count, "Pending image must not match uncaptured OCR text");
                                AssertTrue(ocrHistory.SetOcrResult(imageEntry.Id, recognizedText, OcrStates.Complete), "Synthetic OCR completion must be accepted");
                                ProcessEvents();
                                AssertEqual(1, ocrForm.ClipsListBox.Items.Count, "OCR completion must refresh an active search without manual reload");
                                AssertTrue(ocrForm.OcrPreviewTextBox.Text == recognizedText.Replace("\n", "\r\n"), "Right text preview must render recognized lines with native Windows newlines");
                                AssertTrue(ocrForm.PreviewTextBox.Text == imageBase64, "Left preview must keep base64 visible alongside recognized text");
                                AssertTrue(ocrForm.CopyImageTextButton.Enabled, "Recognized text must enable its copy action");
                                AssertTrue(ocrForm.OcrPreviewTextBox.AccessibleName == "Extracted image text", "OCR preview must have an accessible name");
                                AssertTrue(ocrForm.CopyButton.Text == "Copy image", "New image selections must default to the actual image");

                                ocrForm.CopyImageTextButton.PerformClick();
                                AssertTrue(GetToast(ocrForm).Text.Contains("text copy simulated"), "OCR copy must use its guarded clipboard-free action");
                                AssertTrue(ocrForm.SelectedImageCopyTarget == ImageCopyTarget.OcrText,
                                    "The explicit OCR preview action must select the text target");
                                AssertTrue(ocrHistory.GetOcrText(imageEntry.Id) == recognizedText, "OCR copy must leave the source text intact");
                                AssertTrue(ocrHistory.GetText(imageEntry.Id) == imageBase64, "OCR must preserve original image base64");
                                AssertEqual(1, ocrHistory.Snapshot().Count, "A simulated text copy must not create another history item");

                                GetToast(ocrForm).Text = "";
                                PressTextBoxKey(ocrForm.OcrPreviewTextBox, Keys.Control | Keys.C);
                                AssertTrue(GetToast(ocrForm).Text.Contains("text copy simulated"),
                                    "Ctrl+C in the OCR preview must route the selected text target");
                                GetToast(ocrForm).Text = "";
                                ocrForm.Activate();
                                ocrForm.ClipsListBox.Focus();
                                PressKey(ocrForm, Keys.Enter);
                                AssertTrue(GetToast(ocrForm).Text.Contains("text copy simulated"),
                                    "Enter must copy the selected OCR target");

                                GetToast(ocrForm).Text = "";
                                ocrForm.CopyImageBase64Button.PerformClick();
                                AssertTrue(GetToast(ocrForm).Text.Contains("Base64 copy simulated"),
                                    "The left column must provide the guarded base64 copy action");
                                AssertTrue(ocrForm.SelectedImageCopyTarget == ImageCopyTarget.Base64,
                                    "The explicit base64 preview action must select the base64 target");
                                AssertTrue(ocrForm.CopyImageTextButton.Enabled, "The right column must keep its separate text copy action");
                                VerifyMainFormLayout(ocrForm);
                                ocrForm.Size = new Size(420, 460);
                                ProcessEvents();
                                VerifyMainFormLayout(ocrForm);
                                AssertTrue(ocrForm.OcrPreviewTextBox.Width > 100 && ocrForm.OcrPreviewTextBox.Height >= 64,
                                    "OCR preview must remain readable at minimum size (actual " + ocrForm.OcrPreviewTextBox.Size + ")");

                                ocrHistory.Protect(8101);
                                ProcessEvents();
                                AssertEqual(0, ocrForm.ClipsListBox.Items.Count, "Protected images must no longer match their former OCR text");
                                AssertTrue(!ocrForm.CopyImageTextButton.Enabled, "Protected or removed images must disable Copy text");
                                AssertTrue(!ocrForm.OcrPreviewTextBox.Text.Contains("Synthetic receipt"), "Protected OCR must leave no text in the preview");
                            }
                        }
                    }, ref failureCount);

                    RunTest("Image OCR No Text, Unavailable and Failed States", delegate
                    {
                        string[] statuses = new string[] { OcrStates.NoText, OcrStates.Unavailable, OcrStates.Failed };
                        string[] messages = new string[] { "No text found", "Text recognition is unavailable", "Text recognition could not finish" };
                        for (int stateIndex = 0; stateIndex < statuses.Length; stateIndex++)
                        {
                            string stateDirectory = Path.Combine(tempDir, "OcrState_" + Guid.NewGuid().ToString("N"));
                            string imageBase64 = CreateSyntheticImageBase64();
                            using (HistoryStore stateHistory = new HistoryStore(settings, stateDirectory))
                            {
                                ClipEntry imageEntry = stateHistory.CaptureImage(imageBase64, (uint)(8200 + stateIndex));
                                using (MainForm stateForm = new MainForm(stateHistory, null, stateDirectory, true, false, null))
                                {
                                    stateForm.ShowInTaskbar = false;
                                    stateForm.StartPosition = FormStartPosition.Manual;
                                    stateForm.Location = new Point(-20000, -20000);
                                    stateForm.Show();
                                    stateForm.PreviewToggleButton.PerformClick();
                                    AssertTrue(stateHistory.SetOcrResult(imageEntry.Id, null, statuses[stateIndex]), "Synthetic terminal status must be accepted");
                                    ProcessEvents();
                                    AssertTrue(stateForm.OcrPreviewTextBox.Text.Contains(messages[stateIndex]), "OCR status must explain why extracted text is absent");
                                    AssertTrue(!stateForm.CopyImageTextButton.Enabled, "OCR without text must disable Copy text");
                                    AssertTrue(stateForm.CopyButton.Enabled, "OCR failure or no text must leave image copy available");
                                    AssertTrue(stateForm.PreviewTextBox.Text == imageBase64, "Images without OCR must retain their base64 preview");
                                    stateForm.Size = new Size(420, 460);
                                    ProcessEvents();
                                    VerifyMainFormLayout(stateForm);
                                }
                            }
                        }
                    }, ref failureCount);

                    RunTest("Image Row Target Clicks, Arrow Navigation and Selection Reset", delegate
                    {
                        string targetDirectory = Path.Combine(tempDir, "ImageTargets_" + Guid.NewGuid().ToString("N"));
                        using (HistoryStore targetHistory = new HistoryStore(settings, targetDirectory))
                        {
                            ClipEntry link = targetHistory.Capture("https://example.com/blueprint", 8299);
                            ClipEntry code = targetHistory.Capture("function blueprint() { return true; }", 8300);
                            ClipEntry text = targetHistory.Capture("Ordinary text target", 8301);
                            ClipEntry ready = targetHistory.CaptureImage(CreateSyntheticImageBase64(), 8302);
                            targetHistory.SetOcrResult(ready.Id, "Readable image text", OcrStates.Complete);
                            ClipEntry pending = targetHistory.CaptureImage(CreateSyntheticImageBase64(2027), 8303);
                            using (MainForm targetForm = new MainForm(targetHistory, null, targetDirectory, true, false, null))
                            {
                                ShowTestForm(targetForm);
                                ClipEntry[] expectedOrder = new ClipEntry[] { pending, ready, text, code, link };
                                AssertClipOrder(targetForm, expectedOrder, "Initial newest-first history");
                                VerifyFullWidthClipRows(targetForm);
                                AssertTarget(targetForm, ImageCopyTarget.Image, "Copy image", "Initial image selection");
                                VerifyImageTargetLayout(targetForm, 0, false);
                                VerifyImageTargetLayout(targetForm, 1, true);

                                ClickImageTarget(targetForm, 1, ImageCopyTarget.OcrText);
                                AssertTrue(((ClipEntry)targetForm.ClipsListBox.SelectedItem).Id == ready.Id,
                                    "Clicking an OCR region on another row must first select that row");
                                AssertTarget(targetForm, ImageCopyTarget.OcrText, "Copy text", "OCR region click");
                                AssertClipOrder(targetForm, expectedOrder, "Selecting an image below the first row");
                                ClickImageTarget(targetForm, 1, ImageCopyTarget.Base64);
                                AssertTarget(targetForm, ImageCopyTarget.Base64, "Copy Base64", "Base64 region click");
                                GetToast(targetForm).Text = "";
                                var doubleClick = typeof(Control).GetMethod("OnDoubleClick",
                                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                                doubleClick.Invoke(targetForm.ClipsListBox, new object[] { EventArgs.Empty });
                                AssertTrue(GetToast(targetForm).Text.Contains("Base64 copy simulated"),
                                    "Double-clicking a format must copy the format selected by its mouse-down");
                                ClickImageTarget(targetForm, 1, ImageCopyTarget.Image);
                                AssertTarget(targetForm, ImageCopyTarget.Image, "Copy image", "Image region click");

                                FocusPicker(targetForm);
                                AssertTrue(PressKey(targetForm, Keys.Right), "Right on an image row must be handled");
                                AssertTarget(targetForm, ImageCopyTarget.OcrText, "Copy text", "Right from image");
                                PressKey(targetForm, Keys.Right);
                                AssertTarget(targetForm, ImageCopyTarget.Base64, "Copy Base64", "Right from OCR");
                                PressKey(targetForm, Keys.Right);
                                AssertTarget(targetForm, ImageCopyTarget.Base64, "Copy Base64", "Right boundary");
                                PressKey(targetForm, Keys.Left);
                                AssertTarget(targetForm, ImageCopyTarget.OcrText, "Copy text", "Left from base64");
                                PressKey(targetForm, Keys.Left);
                                AssertTarget(targetForm, ImageCopyTarget.Image, "Copy image", "Left from OCR");
                                PressKey(targetForm, Keys.Left);
                                AssertTarget(targetForm, ImageCopyTarget.Image, "Copy image", "Left boundary");

                                targetForm.SelectImageCopyTarget(ImageCopyTarget.Base64);
                                PressListArrow(targetForm, Keys.Up);
                                AssertTrue(((ClipEntry)targetForm.ClipsListBox.SelectedItem).Id == pending.Id,
                                    "Up must move to the preceding image row");
                                AssertTarget(targetForm, ImageCopyTarget.Image, "Copy image", "Moving to another row");
                                PressKey(targetForm, Keys.Right);
                                AssertTarget(targetForm, ImageCopyTarget.Base64, "Copy Base64", "Pending OCR must be skipped");
                                PressKey(targetForm, Keys.Left);
                                AssertTarget(targetForm, ImageCopyTarget.Image, "Copy image", "Left must skip pending OCR");
                                AssertTrue(!targetForm.SelectImageCopyTarget(ImageCopyTarget.OcrText),
                                    "Pending OCR cannot be selected through another input path");
                                PressListArrow(targetForm, Keys.Down);
                                AssertTarget(targetForm, ImageCopyTarget.Image, "Copy image", "Down to a completed image");
                                AssertClipOrder(targetForm, expectedOrder, "Image target and vertical arrow navigation");

                                // A single vertical history order is shared by every content type.
                                // Horizontal movement is reserved for representations of images.
                                for (int plainIndex = 2; plainIndex < expectedOrder.Length; plainIndex++)
                                {
                                    PressListArrow(targetForm, Keys.Down);
                                    AssertEqual(plainIndex, targetForm.ClipsListBox.SelectedIndex,
                                        "Down must visit the next full-width row regardless of content type");
                                    ClipEntry plain = (ClipEntry)targetForm.ClipsListBox.SelectedItem;
                                    AssertTrue(plain.Id == expectedOrder[plainIndex].Id,
                                        "Vertical navigation must preserve the displayed history order");
                                    AssertTrue(PressKey(targetForm, Keys.Left) && PressKey(targetForm, Keys.Right),
                                        "The picker must suppress native horizontal navigation for text, code and links");
                                    AssertTrue(targetForm.SelectedImageCopyTarget == ImageCopyTarget.Image &&
                                               targetForm.CopyButton.Text == "Copy text",
                                        "Left and Right must not select image formats for text, code or links");
                                    AssertTrue(targetForm.GetImageTargetBounds(plainIndex, ImageCopyTarget.Image).IsEmpty &&
                                               targetForm.GetImageTargetBounds(plainIndex, ImageCopyTarget.OcrText).IsEmpty &&
                                               targetForm.GetImageTargetBounds(plainIndex, ImageCopyTarget.Base64).IsEmpty,
                                        "Non-image clips must not expose image format hit regions");
                                    AssertEqual(plainIndex, targetForm.ClipsListBox.SelectedIndex,
                                        "Horizontal keys must not change the selected non-image clip");
                                    AssertClipOrder(targetForm, expectedOrder, "Selecting " + plain.Kind);
                                    VerifyFullWidthClipRows(targetForm);
                                }
                                PressListArrow(targetForm, Keys.Up);
                                AssertEqual(3, targetForm.ClipsListBox.SelectedIndex,
                                    "Up must return to the preceding code row");
                                targetForm.TestRefresh();
                                ProcessEvents();
                                AssertClipOrder(targetForm, expectedOrder, "Refreshing the selected non-image row");
                                targetForm.TestSelectClip(1);

                                targetForm.SelectImageCopyTarget(ImageCopyTarget.Base64);
                                targetForm.TestApplySearch("Readable");
                                AssertEqual(1, targetForm.ClipsListBox.Items.Count, "OCR search must isolate the selected image");
                                AssertTarget(targetForm, ImageCopyTarget.Base64, "Copy Base64", "Filtering preserves the same selected image");
                                targetForm.SearchTextBox.Focus();
                                targetForm.SearchTextBox.SelectionStart = 3;
                                targetForm.SearchTextBox.SelectionLength = 0;
                                AssertTrue(!PressKey(targetForm, Keys.Left), "Left inside a search query must remain a caret action");
                                AssertTarget(targetForm, ImageCopyTarget.Base64, "Copy Base64", "Search caret movement");
                                targetForm.TestApplySearch("");
                                AssertClipOrder(targetForm, expectedOrder, "Clearing an image OCR search");
                                targetForm.TestSelectClip(0);
                                targetForm.Size = new Size(420, 460);
                                ProcessEvents();
                                VerifyImageTargetLayout(targetForm, 0, false);
                                VerifyImageTargetLayout(targetForm, 1, true);
                                VerifyMainFormLayout(targetForm);
                                VerifyFullWidthClipRows(targetForm);
                            }
                        }
                    }, ref failureCount);

                    RunTest("OCR Completion Preserves Target and Unavailable OCR Is Skipped", delegate
                    {
                        string refreshDirectory = Path.Combine(tempDir, "ImageTargetRefresh_" + Guid.NewGuid().ToString("N"));
                        using (HistoryStore refreshHistory = new HistoryStore(settings, refreshDirectory))
                        {
                            ClipEntry entry = refreshHistory.CaptureImage(CreateSyntheticImageBase64(), 8401);
                            using (MainForm refreshForm = new MainForm(refreshHistory, null, refreshDirectory, true, false, null))
                            {
                                ShowTestForm(refreshForm);
                                AssertTrue(refreshForm.SelectImageCopyTarget(ImageCopyTarget.Base64), "Base64 must be selectable before OCR finishes");
                                refreshHistory.SetOcrResult(entry.Id, "Asynchronously recognized text", OcrStates.Complete);
                                ProcessEvents();
                                AssertTarget(refreshForm, ImageCopyTarget.Base64, "Copy Base64", "OCR completion must not steal the chosen target");
                                refreshForm.SelectImageCopyTarget(ImageCopyTarget.OcrText);
                                refreshHistory.TogglePin(entry.Id);
                                ProcessEvents();
                                AssertTarget(refreshForm, ImageCopyTarget.OcrText, "Copy text", "Pin refresh preserves the current OCR target");
                                refreshForm.TestRefresh();
                                AssertTarget(refreshForm, ImageCopyTarget.OcrText, "Copy text", "Explicit refresh preserves the same image target");

                                string[] missingStates = new string[]
                                {
                                    OcrStates.NoText, OcrStates.Unavailable, OcrStates.Failed, OcrStates.Pending
                                };
                                foreach (string missingState in missingStates)
                                {
                                    refreshHistory.SetOcrResult(entry.Id, null, missingState);
                                    ProcessEvents();
                                    AssertTarget(refreshForm, ImageCopyTarget.Image, "Copy image", "Invalidated OCR target must fall back to image");
                                    AssertTrue(!refreshForm.SelectImageCopyTarget(ImageCopyTarget.OcrText),
                                        "An OCR target without recognized text cannot be selected");
                                    AssertTrue(refreshForm.GetImageTargetBounds(0, ImageCopyTarget.OcrText).IsEmpty,
                                        "An OCR target without text must have no clickable region");
                                    FocusPicker(refreshForm);
                                    PressKey(refreshForm, Keys.Right);
                                    AssertTarget(refreshForm, ImageCopyTarget.Base64, "Copy Base64", "Right skips unavailable OCR");
                                    PressKey(refreshForm, Keys.Left);
                                    AssertTarget(refreshForm, ImageCopyTarget.Image, "Copy image", "Left skips unavailable OCR");
                                    refreshHistory.SetOcrResult(entry.Id, "Restored OCR text", OcrStates.Complete);
                                    ProcessEvents();
                                    AssertTarget(refreshForm, ImageCopyTarget.Image, "Copy image", "OCR arrival preserves the image target");
                                    refreshForm.SelectImageCopyTarget(ImageCopyTarget.OcrText);
                                }
                            }
                        }
                    }, ref failureCount);

                    RunTest("Selected Image Targets Route Copy and Preserve Privacy Guards", delegate
                    {
                        string copyDirectory = Path.Combine(tempDir, "ImageTargetCopy_" + Guid.NewGuid().ToString("N"));
                        DateTime copyUtc = new DateTime(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc);
                        Settings copySettings = new Settings { MaxAgeMinutes = 1 };
                        copySettings.Normalize();
                        using (HistoryStore copyHistory = new HistoryStore(copySettings, copyDirectory, delegate { return copyUtc; }))
                        {
                            string imageBase64 = CreateSyntheticImageBase64();
                            ClipEntry entry = copyHistory.CaptureImage(imageBase64, 8501);
                            copyHistory.SetOcrResult(entry.Id, "Private synthetic image text", OcrStates.Complete);
                            using (MainForm copyForm = new MainForm(copyHistory, null, copyDirectory, true, false, null))
                            {
                                ShowTestForm(copyForm);
                                AssertTrue(!copyForm.DetailPanel.Visible, "Row copy targets must work while preview is collapsed");
                                AssertSelectedCopyRoutes(copyForm, ImageCopyTarget.Image, "Copy image", "image copy simulated");
                                AssertSelectedCopyRoutes(copyForm, ImageCopyTarget.OcrText, "Copy text", "text copy simulated");
                                AssertSelectedCopyRoutes(copyForm, ImageCopyTarget.Base64, "Copy Base64", "Base64 copy simulated");
                                AssertTrue(copyHistory.GetText(entry.Id) == imageBase64, "All copy targets must retain the full original image");
                                AssertTrue(copyHistory.GetOcrText(entry.Id) == "Private synthetic image text", "All copy targets must retain recognized text");
                                AssertEqual(1, copyHistory.Snapshot().Count, "Simulated copies must not alter clipboard history");

                                copyForm.PreviewToggleButton.PerformClick();
                                copyForm.CopyImageTextButton.PerformClick();
                                AssertTarget(copyForm, ImageCopyTarget.OcrText, "Copy text", "Explicit preview text action");
                                AssertTrue(GetToast(copyForm).Text.Contains("text copy simulated"), "Preview text button must copy OCR");
                                copyForm.CopyImageBase64Button.PerformClick();
                                AssertTarget(copyForm, ImageCopyTarget.Base64, "Copy Base64", "Explicit preview base64 action");
                                AssertTrue(GetToast(copyForm).Text.Contains("Base64 copy simulated"), "Preview base64 button must copy base64");
                                AssertSelectedCopyRoutes(copyForm, ImageCopyTarget.Image, "Copy image", "image copy simulated");

                                copyForm.SelectImageCopyTarget(ImageCopyTarget.OcrText);
                                copyHistory.Protect(8501);
                                ProcessEvents();
                                AssertTrue(!copyForm.CopyButton.Enabled, "Protected image copy must be disabled");
                                AssertTrue(!copyForm.SelectImageCopyTarget(ImageCopyTarget.OcrText), "Protected OCR cannot be selected");
                                AssertTrue(copyForm.GetImageTargetBounds(0, ImageCopyTarget.Image).IsEmpty,
                                    "Protected images must not expose an image copy region");
                                GetToast(copyForm).Text = "";
                                FocusPicker(copyForm);
                                PressKey(copyForm, Keys.Enter);
                                PressTextBoxKey(copyForm.OcrPreviewTextBox, Keys.Control | Keys.C);
                                AssertTrue(GetToast(copyForm).Text == "", "Protected image keyboard actions must not copy any payload");
                                AssertTrue(copyForm.OcrPreviewTextBox.Text.IndexOf("Private synthetic", StringComparison.Ordinal) < 0,
                                    "Protected OCR must be removed from preview controls");

                                copyHistory.Remove(entry.Id);
                                entry = copyHistory.CaptureImage(CreateSyntheticImageBase64(2028), 8502);
                                copyHistory.SetOcrResult(entry.Id, "Expiring OCR text", OcrStates.Complete);
                                ProcessEvents();
                                AssertTrue(copyForm.SelectImageCopyTarget(ImageCopyTarget.Base64), "A fresh image must offer base64 before expiring");
                                AssertTrue(copyForm.CopyButton.Enabled, "The expiry fixture must begin with a copyable image");
                                copyUtc = copyUtc.AddMinutes(2);
                                GetToast(copyForm).Text = "";
                                copyForm.CopyButton.PerformClick();
                                AssertTrue(GetToast(copyForm).Text == "", "An expired image must not copy its old selected target");
                                AssertEqual(0, copyForm.ClipsListBox.Items.Count, "Copying an expired image must refresh the empty list");
                            }
                        }
                    }, ref failureCount);

                    RunTest("Live Popup Dismissal Respects Open Settings", delegate
                    {
                        // No WindowsClipboard is constructed; this exercises only form lifecycle behavior.
                        using (MainForm popup = new MainForm(history, null, tempDir, false, false, null))
                        {
                            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                            var settingsOpen = typeof(MainForm).GetField("_settingsOpen", flags);
                            var deactivate = typeof(MainForm).GetMethod("OnDeactivate", flags);
                            popup.ShowInTaskbar = false;
                            popup.StartPosition = FormStartPosition.Manual;
                            popup.Location = new Point(-20000, -20000);
                            settingsOpen.SetValue(popup, true);
                            popup.Show();
                            ProcessEvents();
                            deactivate.Invoke(popup, new object[] { EventArgs.Empty });
                            AssertTrue(popup.Visible, "Opening settings must not dismiss its popup owner");
                            settingsOpen.SetValue(popup, false);
                            deactivate.Invoke(popup, new object[] { EventArgs.Empty });
                            ProcessEvents();
                            AssertTrue(!popup.Visible, "Live popup must dismiss when another app takes focus");
                        }
                    }, ref failureCount);

                    RunTest("Production Clipboard Wiring Captures Without Automatic Protection", delegate
                    {
                        string wiringDirectory = Path.Combine(tempDir, "ProductionWiring_" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(wiringDirectory);
                        Settings wiringSettings = new Settings();
                        wiringSettings.Normalize();
                        using (HistoryStore wiringHistory = new HistoryStore(wiringSettings, wiringDirectory))
                        using (MainForm owner = new MainForm(wiringHistory, null, wiringDirectory, false, false, null))
                        using (WindowsClipboard clipboard = Program.CreateClipboard(owner, wiringHistory, true))
                        {
                            // The listener is paused before registration, and this test never pumps messages while unpaused.
                            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                            Type clipboardType = typeof(WindowsClipboard);
                            AssertTrue(clipboard.Paused, "Production test listener must start paused before any clipboard notification");
                            AssertTrue(clipboardType.GetField("_onProtect", flags).GetValue(clipboard) == null,
                                "Production must not wire a callback that censors or discards captured clips");
                            AssertTrue((IntPtr)clipboardType.GetField("_hookHandle", flags).GetValue(clipboard) == IntPtr.Zero,
                                "Production must not install a keyboard hook for paste detection");
                            AssertTrue(clipboardType.GetField("_nativeThread", flags).GetValue(clipboard) == null,
                                "Production must not start the paste destination worker");
                            AssertTrue(clipboardType.GetField("_uiaThread", flags).GetValue(clipboard) == null,
                                "Production must not start the UI Automation worker");
                            AssertTrue(clipboardType.GetField("_sharedDeadlineTimer", flags).GetValue(clipboard) == null,
                                "Production must not start a protection deadline timer");
                            AssertTrue(clipboard.Status.IndexOf("Keyboard hook", StringComparison.OrdinalIgnoreCase) < 0 &&
                                       clipboard.Status.IndexOf("UI Automation", StringComparison.OrdinalIgnoreCase) < 0,
                                "Status must not warn about intentionally removed paste detection services");

                            clipboardType.GetMethod("OnClipboardUpdated", flags).Invoke(clipboard, new object[0]);
                            object queue = clipboardType.GetField("_uiaQueue", flags).GetValue(clipboard);
                            AssertEqual(0, (int)queue.GetType().GetProperty("Count").GetValue(queue, null),
                                "Clipboard changes must not schedule destination inspection");
                            AssertEqual(0, wiringHistory.Snapshot().Count,
                                "Paused production listener must not read a user clipboard payload");

                            const uint keptSequence = 7001;
                            const string keptPayload = "Synthetic clip remains after a protection dispatch";
                            ClipEntry kept = wiringHistory.Capture(keptPayload, keptSequence);
                            clipboardType.GetMethod("DispatchProtect", flags).Invoke(clipboard, new object[] { keptSequence });
                            AssertTrue(wiringHistory.GetText(kept.Id) == keptPayload,
                                "Dormant protection dispatch must not censor or discard a production clip");

                            const string capturedPayload = "Synthetic clip through the production capture callback";
                            try
                            {
                                clipboard.Paused = false;
                                clipboardType.GetMethod("DispatchCapture", flags).Invoke(clipboard, new object[] { capturedPayload, (uint)7002 });
                            }
                            finally
                            {
                                clipboard.Paused = true;
                            }
                            AssertEqual(2, wiringHistory.Snapshot().Count,
                                "The production capture callback must still save clipboard text");
                            AssertTrue(wiringHistory.Snapshot()[0].Text == capturedPayload,
                                "The production capture callback must preserve plain text");

                            string imageBase64 = CreateSyntheticImageBase64();
                            try
                            {
                                clipboard.Paused = false;
                                clipboardType.GetMethod("DispatchImageCapture", flags).Invoke(clipboard, new object[] { imageBase64, (uint)7003 });
                            }
                            finally
                            {
                                clipboard.Paused = true;
                            }
                            AssertEqual(3, wiringHistory.Snapshot().Count, "The production image callback must save an image clip");
                            ClipEntry imageEntry = wiringHistory.Snapshot()[0];
                            AssertTrue(imageEntry.Kind == "IMAGE", "Production image callback must preserve image classification");
                            AssertTrue(imageEntry.Text == imageBase64, "Production image callback must preserve full raw base64");
                        }
                    }, ref failureCount);

                    // 8. SettingsDialog Layout & Non-Intersection Tests
                    RunTest("SettingsDialog Bounds at Minimum Size (520x520)", delegate
                    {
                        using (SettingsDialog dlg = new SettingsDialog(settings, history, tempDir, true))
                        {
                            dlg.ShowInTaskbar = false;
                            dlg.StartPosition = FormStartPosition.Manual;
                            dlg.Location = new Point(-20000, -20000);
                            dlg.Show();
                            Application.DoEvents();

                            // Verify initial clamped outer size before test resizes
                            Screen dlgScreen = Screen.PrimaryScreen;
                            try
                            {
                                dlgScreen = Screen.FromControl(dlg) ?? Screen.PrimaryScreen;
                            }
                            catch
                            {
                            }
                            Rectangle dlgWorkArea = (dlgScreen != null) ? dlgScreen.WorkingArea : Screen.PrimaryScreen.WorkingArea;
                            Size expectedDlgInit, expectedDlgMin;
                            MainForm.ClampWindowSize(dlgWorkArea, new Size(560, 620), new Size(520, 520), out expectedDlgInit, out expectedDlgMin);
                            AssertEqual(expectedDlgInit.Width, dlg.Size.Width,
                                "SettingsDialog actual outer width must match clamped initial width (not inflated by client chrome)");
                            AssertEqual(expectedDlgInit.Height, dlg.Size.Height,
                                "SettingsDialog actual outer height must match clamped initial height (not inflated by client chrome)");

                            dlg.Size = new Size(520, 520);
                            dlg.PerformLayout();
                            ProcessEvents();

                            VerifySettingsDialogLayout(dlg);
                        }
                    }, ref failureCount);

                    RunTest("Settings Expose Retention and Storage Without Automatic Protection", delegate
                    {
                        using (SettingsDialog dlg = new SettingsDialog(settings, history, tempDir, true))
                        {
                            AssertNoAutomaticProtectionControls(dlg);
                        }
                    }, ref failureCount);

                    RunTest("General Settings Save and Reload", delegate
                    {
                        string settingsDirectory = Path.Combine(tempDir, "GeneralSettings_" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(settingsDirectory);
                        Settings initialSettings = new Settings();
                        initialSettings.MaxAgeMinutes = 180;
                        initialSettings.MaxItems = 120;
                        initialSettings.PersistHistory = false;
                        initialSettings.Normalize();
                        using (HistoryStore settingsHistory = new HistoryStore(initialSettings, settingsDirectory))
                        {
                            using (SettingsDialog dlg = new SettingsDialog(initialSettings, settingsHistory, settingsDirectory, true))
                            {
                                dlg.StartPosition = FormStartPosition.Manual;
                                dlg.Location = new Point(-20000, -20000);
                                dlg.Show();
                                ProcessEvents();
                                AssertEqual(180, (int)dlg.MaxAgeNumeric.Value, "Dialog must load the saved retention");
                                AssertEqual(120, (int)dlg.MaxItemsNumeric.Value, "Dialog must load the saved capacity");
                                AssertTrue(!dlg.PersistHistoryCheckBox.Checked, "Dialog must load session-only history");
                                dlg.MaxAgeNumeric.Value = 240;
                                dlg.MaxItemsNumeric.Value = 200;
                                dlg.PersistHistoryCheckBox.Checked = true;
                                dlg.SaveButton.PerformClick();
                                ProcessEvents();
                                AssertTrue(dlg.DialogResult == DialogResult.OK, "Saving general settings must complete successfully");
                            }

                            AssertEqual(240, settingsHistory.Settings.MaxAgeMinutes, "Saving must apply retention to the running history");
                            AssertEqual(200, settingsHistory.Settings.MaxItems, "Saving must apply capacity to the running history");
                            AssertTrue(settingsHistory.Settings.PersistHistory, "Saving must apply encrypted persistence");
                            Settings reloaded = SettingsStore.Load(settingsDirectory);
                            AssertTrue(reloaded != null, "Settings must reload from disk");
                            AssertEqual(240, reloaded.MaxAgeMinutes, "Reload must preserve retention");
                            AssertEqual(200, reloaded.MaxItems, "Reload must preserve capacity");
                            AssertTrue(reloaded.PersistHistory, "Reload must preserve persistence");
                            using (SettingsDialog reopened = new SettingsDialog(reloaded, settingsHistory, settingsDirectory, true))
                            {
                                AssertEqual(240, (int)reopened.MaxAgeNumeric.Value, "Reopened dialog must display saved retention");
                                AssertEqual(200, (int)reopened.MaxItemsNumeric.Value, "Reopened dialog must display saved capacity");
                                AssertTrue(reopened.PersistHistoryCheckBox.Checked, "Reopened dialog must display saved persistence");
                                AssertNoAutomaticProtectionControls(reopened);
                            }
                        }
                    }, ref failureCount);

                    // 9. CLI Argument Parser Tests
                    RunTest("CLI Argument Parser Seams", delegate
                    {
                        // Default / Normal
                        CommandLineOptions optNormal = Program.ParseArguments(new string[0]);
                        AssertEqual((int)CommandLineMode.Normal, (int)optNormal.Mode, "Empty args must result in Normal mode");

                        // Demo
                        CommandLineOptions optDemo = Program.ParseArguments(new string[] { "--demo" });
                        AssertEqual((int)CommandLineMode.Demo, (int)optDemo.Mode, "--demo must result in Demo mode");

                        // Valid Preview
                        string previewFile = Path.Combine(tempDir, "preview.png");
                        CommandLineOptions optPreview = Program.ParseArguments(new string[] { "--preview", previewFile });
                        AssertEqual((int)CommandLineMode.Preview, (int)optPreview.Mode, "--preview with absolute png must result in Preview mode");
                        AssertTrue(string.Equals(optPreview.PreviewOutputPath, previewFile, StringComparison.OrdinalIgnoreCase),
                            "PreviewOutputPath must match argument");

                        // Valid path with spaces
                        string previewSpaces = Path.Combine(tempDir, "my test dir with spaces", "preview output.png");
                        CommandLineOptions optSpaces = Program.ParseArguments(new string[] { "--preview", previewSpaces });
                        AssertEqual((int)CommandLineMode.Preview, (int)optSpaces.Mode, "Preview path with spaces must be accepted");
                        AssertTrue(string.Equals(optSpaces.PreviewOutputPath, previewSpaces, StringComparison.OrdinalIgnoreCase),
                            "PreviewOutputPath must preserve spaces");

                        // Pure IsFullyQualifiedPngPath tests: valid internal spaces vs rejected leading/trailing whitespace
                        AssertTrue(Program.IsFullyQualifiedPngPath(@"C:\folder with spaces\preview.png"),
                            "IsFullyQualifiedPngPath must accept valid internal spaces");
                        AssertTrue(!Program.IsFullyQualifiedPngPath(@" C:\folder with spaces\preview.png"),
                            "IsFullyQualifiedPngPath must reject leading whitespace");
                        AssertTrue(!Program.IsFullyQualifiedPngPath(@"C:\folder with spaces\preview.png "),
                            "IsFullyQualifiedPngPath must reject trailing whitespace");
                        AssertTrue(!Program.IsFullyQualifiedPngPath(" " + previewFile),
                            "IsFullyQualifiedPngPath must reject leading space");
                        AssertTrue(!Program.IsFullyQualifiedPngPath(previewFile + " "),
                            "IsFullyQualifiedPngPath must reject trailing space");

                        // ParseArguments rejection of leading/trailing whitespace
                        CommandLineOptions optLeadingSpace = Program.ParseArguments(new string[] { "--preview", " " + previewFile });
                        AssertEqual((int)CommandLineMode.Invalid, (int)optLeadingSpace.Mode,
                            "Preview path with leading whitespace must be Invalid");

                        CommandLineOptions optTrailingSpace = Program.ParseArguments(new string[] { "--preview", previewFile + " " });
                        AssertEqual((int)CommandLineMode.Invalid, (int)optTrailingSpace.Mode,
                            "Preview path with trailing whitespace must be Invalid");

                        // Valid UNC path
                        CommandLineOptions optUnc = Program.ParseArguments(new string[] { "--preview", @"\\server\share\preview.png" });
                        AssertEqual((int)CommandLineMode.Preview, (int)optUnc.Mode, "Valid UNC path must result in Preview mode");

                        // Incomplete UNC path (no share component)
                        CommandLineOptions optUncNoShare = Program.ParseArguments(new string[] { "--preview", @"\\server\preview.png" });
                        AssertEqual((int)CommandLineMode.Invalid, (int)optUncNoShare.Mode, "Incomplete UNC path must be Invalid");

                        // Drive-relative path (e.g. C:relative.png)
                        CommandLineOptions optDriveRel = Program.ParseArguments(new string[] { "--preview", "C:relative.png" });
                        AssertEqual((int)CommandLineMode.Invalid, (int)optDriveRel.Mode, "Drive-relative path C:relative.png must be Invalid");

                        // Root-relative path (e.g. \relative.png)
                        CommandLineOptions optRootRel = Program.ParseArguments(new string[] { "--preview", @"\relative.png" });
                        AssertEqual((int)CommandLineMode.Invalid, (int)optRootRel.Mode, @"Root-relative path \relative.png must be Invalid");

                        // Relative preview path
                        CommandLineOptions optRel = Program.ParseArguments(new string[] { "--preview", "test.png" });
                        AssertEqual((int)CommandLineMode.Invalid, (int)optRel.Mode, "Relative preview path must be Invalid");
                        AssertTrue(optRel.ErrorMessage != null && optRel.ErrorMessage.IndexOf("absolute", StringComparison.OrdinalIgnoreCase) >= 0,
                            "Error message should mention absolute path requirement");

                        // Non-PNG preview path
                        CommandLineOptions optBmp = Program.ParseArguments(new string[] { "--preview", Path.Combine(tempDir, "test.bmp") });
                        AssertEqual((int)CommandLineMode.Invalid, (int)optBmp.Mode, "Non-PNG preview path must be Invalid");
                        AssertTrue(optBmp.ErrorMessage != null && optBmp.ErrorMessage.IndexOf(".png", StringComparison.OrdinalIgnoreCase) >= 0,
                            "Error message should mention .png requirement");

                        // Missing preview path
                        CommandLineOptions optMissing = Program.ParseArguments(new string[] { "--preview" });
                        AssertEqual((int)CommandLineMode.Invalid, (int)optMissing.Mode, "Missing preview path must be Invalid");

                        // Conflicting arguments
                        CommandLineOptions optConflict = Program.ParseArguments(new string[] { "--demo", "--preview", previewFile });
                        AssertEqual((int)CommandLineMode.Invalid, (int)optConflict.Mode, "Conflicting arguments must be Invalid");

                        // Unknown arguments
                        CommandLineOptions optUnknown = Program.ParseArguments(new string[] { "--unknown-argument" });
                        AssertEqual((int)CommandLineMode.Invalid, (int)optUnknown.Mode, "Unknown arguments must be Invalid");
                    }, ref failureCount);

                    // 10. Injected-Clock Two-Clip Expiration Regression (No Stale Detail Overwrite)
                    RunTest("Injected-Clock Two-Clip Expiration Regression", delegate
                    {
                        string subTempDir = Path.Combine(tempDir, "ClockRegression_" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(subTempDir);

                        DateTime fakeUtc = new DateTime(2026, 9, 7, 10, 0, 0, DateTimeKind.Utc);
                        Func<DateTime> clock = delegate { return fakeUtc; };

                        Settings regSettings = new Settings();
                        regSettings.MaxAgeMinutes = 1; // 1 minute retention
                        regSettings.Normalize();

                        using (HistoryStore regHistory = new HistoryStore(regSettings, subTempDir, clock))
                        {
                            // Older clip captured at T0
                            regHistory.Capture("Older clip payload - will expire first", 101);

                            // Advance clock by 30 seconds
                            fakeUtc = fakeUtc.AddSeconds(30);

                            // Newer clip captured at T0 + 30s
                            regHistory.Capture("Newer clip payload - will survive", 102);

                            using (MainForm regForm = new MainForm(regHistory, null, subTempDir, true, false, null))
                            {
                                regForm.ShowInTaskbar = false;
                                regForm.StartPosition = FormStartPosition.Manual;
                                regForm.Location = new Point(-20000, -20000);
                                regForm.Show();
                                Application.DoEvents();

                                // Select older clip
                                int olderIndex = -1;
                                for (int i = 0; i < regForm.ClipsListBox.Items.Count; i++)
                                {
                                    ClipEntry c = regForm.ClipsListBox.Items[i] as ClipEntry;
                                    if (c != null && c.Text != null && c.Text.IndexOf("Older clip") >= 0)
                                    {
                                        olderIndex = i;
                                        break;
                                    }
                                }
                                AssertTrue(olderIndex >= 0, "Older clip must be found in list box");
                                regForm.TestSelectClip(olderIndex);
                                ProcessEvents();

                                AssertTrue(regForm.PreviewTextBox.Text.IndexOf("Older clip") >= 0,
                                    "Initial detail should display older clip text");

                                // Advance clock by 40 seconds: total 70 seconds elapsed since T0.
                                // Older clip (age 70s > 60s) is expired! Newer clip (age 40s < 60s) survives.
                                fakeUtc = fakeUtc.AddSeconds(40);

                                // Perform selection action on the list without manual refresh
                                regForm.TestSelectClip(0);
                                ProcessEvents();

                                // Surviving clip must be displayed cleanly without stale overwrite or expired message
                                AssertTrue(regForm.PreviewTextBox.Text.IndexOf("Newer clip payload - will survive") >= 0,
                                    "Detail pane must display surviving clip without stale overwrite");
                                AssertTrue(regForm.PreviewTextBox.Text.IndexOf("[Clip has expired") < 0,
                                    "Detail pane must NOT show expired placeholder when surviving clip is selected");
                                AssertTrue(regForm.CopyButton.Enabled,
                                    "Copy button should be enabled for surviving plain text clip");
                            }
                        }
                    }, ref failureCount);
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Unhandled exception during UI test run: " + ex.ToString());
                Console.ResetColor();
                failureCount++;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
                catch
                {
                }
            }

            Console.WriteLine("========================================");
            if (failureCount == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("ALL UI TESTS PASSED SUCCESSFULLY (0 failures)");
                Console.ResetColor();
                return 0;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(string.Format("UI TESTS FAILED WITH {0} FAILURE(S)", failureCount));
                Console.ResetColor();
                return 1;
            }
        }

        private static void VerifyMainFormLayout(MainForm form)
        {
            // The compact popup dedicates its width to history and keeps actions visible.
            AssertTrue(form.MainPanel.Width > 0, "MainPanel width must be positive");
            AssertTrue(form.StatusBarPanel.Height > 0, "StatusBar height must be positive");
            AssertTrue(form.HeaderPanel.Height > 0, "Header height must be positive");
            AssertTrue(form.SearchFilterPanel.Height > 0, "SearchFilter height must be positive");
            AssertTrue(form.ContentPanel.Height > 0, "ContentPanel height must be positive");
            AssertTrue(form.ListContainerPanel.Width > 0, "ListContainer width must be positive");
            AssertTrue(form.ClipsListBox.Width > 0, "ListBox width must be positive");
            AssertTrue(form.DetailToolbarPanel.Visible, "Clip actions must remain visible with preview collapsed or expanded");
            AssertTrue(form.DetailToolbarPanel.Parent == form.MainPanel, "Clip actions must live outside the optional preview");
            AssertFits(form.MainPanel, form, "MainPanel");
            AssertFits(form.StatusBarPanel, form, "StatusBarPanel");
            AssertNoIntersection(form.StatusBarPanel.Bounds, form.MainPanel.Bounds, "StatusBarPanel", "MainPanel");

            Control[] mainRegions = new Control[]
            {
                form.HeaderPanel, form.SearchFilterPanel, form.TypeFilterPanel, form.ContentPanel, form.DetailToolbarPanel
            };
            AssertNonOverlappingChildren(mainRegions, form.MainPanel, "Main region");

            Control[] searchControls = new Control[]
            {
                form.SearchTextBox, form.NavAllButton, form.NavPinnedButton
            };
            AssertNonOverlappingChildren(searchControls, form.SearchFilterPanel, "Search/filter control");
            AssertNonOverlappingChildren(new Control[]
            {
                form.FilterAllButton, form.FilterTextButton, form.FilterLinkButton, form.FilterCodeButton, form.FilterImageButton
            }, form.TypeFilterPanel, "Content type filter");

            Control[] headerControls = new Control[]
            {
                form.HeaderTitleLabel, form.CloseButton
            };
            AssertNonOverlappingChildren(headerControls, form.HeaderPanel, "Header control");

            Control[] actionControls = new Control[]
            {
                form.CopyButton, form.PinButton, form.DeleteButton, form.PreviewToggleButton
            };
            AssertNonOverlappingChildren(actionControls, form.DetailToolbarPanel, "Clip action");

            AssertFits(form.ListContainerPanel, form.ContentPanel, "ListContainerPanel");
            AssertFits(form.ClipsListBox, form.ListContainerPanel, "ClipsListBox");
            if (form.DetailPanel.Visible)
            {
                AssertTrue(form.DetailPanel.Dock == DockStyle.Bottom, "Preview must expand below the clip list");
                AssertTrue(form.DetailContentPanel.Visible, "Expanded preview must expose details for the selected clip");
                AssertFits(form.DetailPanel, form.ContentPanel, "DetailPanel");
                AssertNoIntersection(form.ListContainerPanel.Bounds, form.DetailPanel.Bounds, "ListContainerPanel", "DetailPanel");
                AssertFits(form.DetailTextContainerPanel, form.DetailContentPanel, "DetailTextContainerPanel");
                if (form.ImageActionsPanel.Visible)
                {
                    AssertTrue(!form.DetailMetaPanel.Visible, "Image actions must replace metadata to preserve preview space");
                    AssertFits(form.ImageActionsPanel, form.DetailContentPanel, "ImageActionsPanel");
                    AssertNoIntersection(form.ImageActionsPanel.Bounds, form.DetailTextContainerPanel.Bounds,
                        "ImageActionsPanel", "DetailTextContainerPanel");
                    AssertNonOverlappingChildren(new Control[]
                    {
                        form.CopyImageBase64Button, form.CopyImageTextButton
                    }, form.ImageActionsPanel, "Image preview action");
                    AssertTrue(form.OcrPreviewPanel.Visible, "OCR and base64 must be visible simultaneously for images");
                    AssertFits(form.PreviewTextBox, form.DetailTextContainerPanel, "Base64 preview");
                    AssertFits(form.OcrPreviewPanel, form.DetailTextContainerPanel, "OCR preview column");
                    AssertFits(form.OcrPreviewTextBox, form.OcrPreviewPanel, "OCR text preview");
                    AssertNoIntersection(form.PreviewTextBox.Bounds, form.OcrPreviewPanel.Bounds,
                        "Base64 preview", "OCR preview column");
                    AssertTrue(form.PreviewTextBox.Right <= form.OcrPreviewPanel.Left,
                        "The base64 value must be on the left and OCR on the right");
                }
                else
                {
                    AssertFits(form.DetailMetaPanel, form.DetailContentPanel, "DetailMetaPanel");
                    AssertNoIntersection(form.DetailMetaPanel.Bounds, form.DetailTextContainerPanel.Bounds,
                        "DetailMetaPanel", "DetailTextContainerPanel");
                    Control[] detailLabels = new Control[]
                    {
                        form.DetailKindLabel, form.DetailTimeLabel, form.CharCountLabel
                    };
                    AssertNonOverlappingChildren(detailLabels, form.DetailMetaPanel, "Preview metadata");
                }
            }

            // Footer feedback and clipboard controls must remain separate, including during copy.
            AssertNonOverlappingChildren(new Control[]
            {
                form.PauseResumeButton, form.SettingsButton
            }, form.StatusBarPanel, "Bottom-right control");
            Control[] feedback = new Control[] { form.StatusLeftLabel, form.StatusRightLabel, GetToast(form) };
            for (int i = 0; i < feedback.Length; i++)
            {
                if (!feedback[i].Visible) continue;
                AssertFits(feedback[i], form.StatusBarPanel, "Footer feedback");
                AssertNoIntersection(feedback[i].Bounds, form.PauseResumeButton.Bounds, "Footer feedback", "Pause");
                AssertNoIntersection(feedback[i].Bounds, form.SettingsButton.Bounds, "Footer feedback", "Settings");
                for (int j = i + 1; j < feedback.Length; j++)
                {
                    if (feedback[j].Visible)
                        AssertNoIntersection(feedback[i].Bounds, feedback[j].Bounds, "Footer feedback " + i, "Footer feedback " + j);
                }
            }
        }

        private static int CountVisibleTitleLabels(Control parent)
        {
            int count = 0;
            foreach (Control control in parent.Controls)
            {
                if (!control.Visible) continue;
                if (control is Label && control.Text == "Dolly Paste") count++;
                count += CountVisibleTitleLabels(control);
            }
            return count;
        }

        private static void AssertNoBrandTagline(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                if (!control.Visible) continue;
                AssertTrue(control.Text.IndexOf("by SSS", StringComparison.OrdinalIgnoreCase) < 0 &&
                           control.Text.IndexOf("Local clipboard", StringComparison.OrdinalIgnoreCase) < 0,
                    "The compact popup must not repeat the removed brand/local clipboard tagline");
                AssertNoBrandTagline(control);
            }
        }

        private static void AssertClipOrder(MainForm form, ClipEntry[] expected, string reason)
        {
            AssertEqual(expected.Length, form.ClipsListBox.Items.Count, reason + ": item count");
            for (int i = 0; i < expected.Length; i++)
                AssertTrue(((ClipEntry)form.ClipsListBox.Items[i]).Id == expected[i].Id,
                    reason + ": selecting a clip or format must not reorder history at index " + i);
        }

        private static void VerifyFullWidthClipRows(MainForm form)
        {
            Rectangle first = Rectangle.Empty;
            Rectangle previous = Rectangle.Empty;
            for (int i = 0; i < form.ClipsListBox.Items.Count; i++)
            {
                Rectangle row = form.ClipsListBox.GetItemRectangle(i);
                Rectangle card = form.GetClipCardBounds(i);
                AssertTrue(card.Width >= form.ClipsListBox.ClientSize.Width - 24,
                    "Every history card must use the full available row width with compact side gutters");
                AssertTrue(row.Contains(card), "Each clipboard card must fit within its own vertical history row");
                if (i == 0) first = card;
                else
                {
                    AssertEqual(first.Left, card.Left, "All content types must share the same left edge");
                    AssertEqual(first.Right, card.Right, "All content types must share the same right edge");
                    AssertTrue(card.Top >= previous.Bottom, "Clipboard items must stack vertically without overlapping");
                }
                previous = card;
            }
        }

        private static void AssertFits(Control child, Control parent, string name)
        {
            AssertTrue(child.Width > 0 && child.Height > 0, name + " must have positive dimensions");
            AssertTrue(parent.ClientRectangle.Contains(child.Bounds),
                string.Format("{0} bounds {1} must fit its container {2}", name, child.Bounds, parent.ClientRectangle));
        }

        private static void AssertNonOverlappingChildren(Control[] children, Control parent, string description)
        {
            for (int i = 0; i < children.Length; i++)
            {
                AssertFits(children[i], parent, description + " " + i);
                for (int j = i + 1; j < children.Length; j++)
                {
                    AssertNoIntersection(children[i].Bounds, children[j].Bounds,
                        description + " " + i, description + " " + j);
                }
            }
        }

        private static void ShowTestForm(MainForm form)
        {
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-20000, -20000);
            form.Show();
            ProcessEvents();
        }

        private static void FocusPicker(MainForm form)
        {
            form.Activate();
            form.ClipsListBox.Focus();
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        private static void PressListArrow(MainForm form, Keys key)
        {
            FocusPicker(form);
            // Native ListBox navigation follows ProcessCmdKey, so exercise the
            // actual control rather than invoking only the form's shortcut hook.
            SendMessage(form.ClipsListBox.Handle, 0x0100, new IntPtr((int)key), IntPtr.Zero);
            SendMessage(form.ClipsListBox.Handle, 0x0101, new IntPtr((int)key), IntPtr.Zero);
            ProcessEvents();
        }

        private static void AssertTarget(MainForm form, ImageCopyTarget expected, string expectedLabel, string reason)
        {
            AssertTrue(form.SelectedImageCopyTarget == expected,
                reason + ": expected " + expected + ", got " + form.SelectedImageCopyTarget);
            AssertTrue(form.CopyButton.Text == expectedLabel,
                reason + ": primary copy label must identify the selected target");
            AssertTrue((form.CopyButton.AccessibleName ?? "").Contains(expectedLabel),
                reason + ": accessible copy label must identify the selected target");
            AssertTrue((form.ClipsListBox.AccessibleDescription ?? "").Contains(expectedLabel),
                reason + ": keyboard target selection must have an accessible description");
        }

        private static void ClickImageTarget(MainForm form, int index, ImageCopyTarget target)
        {
            Rectangle bounds = form.GetImageTargetBounds(index, target);
            AssertTrue(!bounds.IsEmpty, "The requested image target must have a hit region");
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var mouseDown = typeof(MainForm).GetMethod("OnClipsMouseDown", flags);
            AssertTrue(mouseDown != null, "Image row mouse handler must be present");
            mouseDown.Invoke(form, new object[] { form.ClipsListBox,
                new MouseEventArgs(MouseButtons.Left, 1, bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2, 0) });
        }

        private static void VerifyImageTargetLayout(MainForm form, int index, bool hasOcr)
        {
            Rectangle row = form.ClipsListBox.GetItemRectangle(index);
            Rectangle image = form.GetImageTargetBounds(index, ImageCopyTarget.Image);
            Rectangle ocr = form.GetImageTargetBounds(index, ImageCopyTarget.OcrText);
            Rectangle base64 = form.GetImageTargetBounds(index, ImageCopyTarget.Base64);
            AssertTrue(image.Width >= 24 && image.Height >= 24, "Image target must have a usable click area");
            AssertTrue(base64.Width >= 24 && base64.Height >= 24, "Base64 target must have a usable click area");
            AssertTrue(row.Contains(image) && row.Contains(base64), "Image and base64 targets must fit their history row");
            AssertTrue(form.ClipsListBox.ClientRectangle.Contains(image) && form.ClipsListBox.ClientRectangle.Contains(base64),
                "Visible-row image targets must fit the list viewport");
            AssertNoIntersection(image, base64, "Image target", "Base64 target");
            AssertTrue(image.Right <= base64.Left, "Image must precede base64 in the target order");
            if (hasOcr)
            {
                AssertTrue(ocr.Width >= 24 && ocr.Height >= 24, "OCR target must have a usable click area");
                AssertTrue(row.Contains(ocr), "OCR target must fit its history row");
                AssertNoIntersection(image, ocr, "Image target", "OCR target");
                AssertNoIntersection(ocr, base64, "OCR target", "Base64 target");
                AssertTrue(image.Right <= ocr.Left && ocr.Right <= base64.Left,
                    "OCR target must sit between the image and base64 targets");
            }
            else
            {
                AssertTrue(ocr.IsEmpty, "An image without OCR text must not offer an OCR click target");
            }
        }

        private static void AssertSelectedCopyRoutes(MainForm form, ImageCopyTarget target, string label, string expectedToast)
        {
            AssertTrue(form.SelectImageCopyTarget(target), "Available image target must be selectable");
            AssertTarget(form, target, label, "Copy routing target");
            GetToast(form).Text = "";
            form.CopyButton.PerformClick();
            AssertTrue(GetToast(form).Text.Contains(expectedToast), "Primary copy must route the selected " + target + " target");

            GetToast(form).Text = "";
            FocusPicker(form);
            AssertTrue(PressKey(form, Keys.Enter), "Enter must be handled by the focused picker");
            AssertTrue(GetToast(form).Text.Contains(expectedToast), "Enter must route the selected " + target + " target");
            GetToast(form).Text = "";
            AssertTrue(PressKey(form, Keys.Control | Keys.C), "Ctrl+C must be handled by the focused picker");
            AssertTrue(GetToast(form).Text.Contains(expectedToast), "Picker Ctrl+C must route the selected " + target + " target");

            TextBox[] previews = new TextBox[] { form.PreviewTextBox, form.OcrPreviewTextBox };
            foreach (TextBox preview in previews)
            {
                GetToast(form).Text = "";
                AssertTrue(PressTextBoxKey(preview, Keys.Control | Keys.C), "Preview Ctrl+C must use the guarded copy route");
                AssertTrue(GetToast(form).Text.Contains(expectedToast), "Preview Ctrl+C must route the selected " + target + " target");
                GetToast(form).Text = "";
                SendTextBoxCopyMessage(preview);
                AssertTrue(GetToast(form).Text.Contains(expectedToast), "Native preview copy must route the selected " + target + " target");
            }
        }

        private static bool PressKey(MainForm form, Keys keys)
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var processKey = typeof(MainForm).GetMethod("ProcessCmdKey", flags);
            Message message = Message.Create(form.Handle, 0x0100, IntPtr.Zero, IntPtr.Zero);
            return (bool)processKey.Invoke(form, new object[] { message, keys });
        }

        private static bool PressTextBoxKey(TextBox textBox, Keys keys)
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var processKey = typeof(PrivacyTextBox).GetMethod("ProcessCmdKey", flags);
            Message message = Message.Create(textBox.Handle, 0x0100, IntPtr.Zero, IntPtr.Zero);
            return (bool)processKey.Invoke(textBox, new object[] { message, keys });
        }

        private static void SendTextBoxCopyMessage(TextBox textBox)
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var wndProc = typeof(PrivacyTextBox).GetMethod("WndProc", flags);
            Message message = Message.Create(textBox.Handle, 0x0301, IntPtr.Zero, IntPtr.Zero);
            wndProc.Invoke(textBox, new object[] { message });
        }

        private static Label GetToast(MainForm form)
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            return (Label)typeof(MainForm).GetField("_lblToast", flags).GetValue(form);
        }

        private static void VerifySettingsDialogLayout(SettingsDialog dlg)
        {
            // 1. Positive dimensions
            AssertTrue(dlg.HeaderPanel.Height > 0, "SettingsDialog HeaderPanel height must be positive");
            AssertTrue(dlg.BodyPanel.Height > 0, "SettingsDialog BodyPanel height must be positive");
            AssertTrue(dlg.BottomPanel.Height > 0, "SettingsDialog BottomPanel height must be positive");
            AssertTrue(dlg.SaveButton.Width > 0 && dlg.SaveButton.Height > 0, "SaveButton dimensions must be positive");
            AssertTrue(dlg.CancelActionButton.Width > 0 && dlg.CancelActionButton.Height > 0, "CancelActionButton dimensions must be positive");

            // 2. Buttons in BottomPanel reachable and non-overlapping
            AssertTrue(dlg.SaveButton.Right <= dlg.CancelActionButton.Left,
                string.Format("SaveButton (Right={0}) must precede CancelActionButton (Left={1})",
                    dlg.SaveButton.Right, dlg.CancelActionButton.Left));
            AssertTrue(dlg.CancelActionButton.Right <= dlg.BottomPanel.ClientSize.Width,
                string.Format("CancelActionButton (Right={0}) must be within BottomPanel (Width={1})",
                    dlg.CancelActionButton.Right, dlg.BottomPanel.ClientSize.Width));
            AssertTrue(dlg.SaveButton.Left >= 0, "SaveButton Left must be non-negative");
            AssertTrue(dlg.SaveButton.Bottom <= dlg.BottomPanel.ClientSize.Height,
                "SaveButton Bottom must fit within BottomPanel height");
            AssertTrue(dlg.CancelActionButton.Bottom <= dlg.BottomPanel.ClientSize.Height,
                "CancelActionButton Bottom must fit within BottomPanel height");
            AssertNoIntersection(dlg.SaveButton.Bounds, dlg.CancelActionButton.Bounds, "SaveButton", "CancelActionButton");

            // 3. Panel non-intersection
            AssertNoIntersection(dlg.HeaderPanel.Bounds, dlg.BodyPanel.Bounds, "HeaderPanel", "BodyPanel");
            AssertNoIntersection(dlg.BodyPanel.Bounds, dlg.BottomPanel.Bounds, "BodyPanel", "BottomPanel");
            AssertNoIntersection(dlg.HeaderPanel.Bounds, dlg.BottomPanel.Bounds, "HeaderPanel", "BottomPanel");

            // 4. Body controls within BodyPanel bounds
            int bodyAvailWidth = dlg.BodyPanel.ClientSize.Width;
            AssertTrue(dlg.NoticePanel.Right <= bodyAvailWidth, "NoticePanel Right must fit within BodyPanel width");
            AssertTrue(dlg.NoticePanel.Left >= 0, "NoticePanel Left must be >= 0");
            AssertTrue(dlg.PersistHistoryCheckBox.Right <= bodyAvailWidth, "PersistHistoryCheckBox Right must fit within BodyPanel width");
        }

        private static void AssertNoAutomaticProtectionControls(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                AssertTrue(!(control is ComboBox), "Settings must not expose a censor/discard behavior selector");
                if (control is CheckBox || control is Button)
                {
                    string description = (control.Text ?? "") + " " + (control.AccessibleName ?? "");
                    AssertTrue(description.IndexOf("censor", StringComparison.OrdinalIgnoreCase) < 0 &&
                               description.IndexOf("discard", StringComparison.OrdinalIgnoreCase) < 0 &&
                               description.IndexOf("unrecognized", StringComparison.OrdinalIgnoreCase) < 0 &&
                               description.IndexOf("unknown", StringComparison.OrdinalIgnoreCase) < 0 &&
                               description.IndexOf("destination", StringComparison.OrdinalIgnoreCase) < 0 &&
                               description.IndexOf("sensitive", StringComparison.OrdinalIgnoreCase) < 0,
                        "Settings must not expose automatic protection controls");
                }
                AssertNoAutomaticProtectionControls(control);
            }
        }

        private static void AssertNoIntersection(Rectangle r1, Rectangle r2, string name1, string name2)
        {
            Rectangle inter = Rectangle.Intersect(r1, r2);
            if (!inter.IsEmpty && inter.Width > 0 && inter.Height > 0)
            {
                throw new Exception(string.Format("Controls collide: {0} ({1}) intersects {2} ({3}), intersection={4}",
                    name1, r1, name2, r2, inter));
            }
        }

        private static void ProcessEvents()
        {
            for (int i = 0; i < 5; i++)
            {
                Application.DoEvents();
            }
        }

        private static string CreateSyntheticImageBase64(int seed = 2026)
        {
            using (Bitmap bitmap = new Bitmap(160, 120))
            {
                Random random = new Random(seed);
                for (int y = 0; y < bitmap.Height; y++)
                {
                    for (int x = 0; x < bitmap.Width; x++)
                        bitmap.SetPixel(x, y, Color.FromArgb(random.Next(256), random.Next(256), random.Next(256)));
                }
                return ImagePayload.EncodePng(bitmap);
            }
        }

        private static void AssertImageDisposed(Image image, string message)
        {
            AssertTrue(image != null, "Image fixture must exist before disposal");
            bool disposed = false;
            try { int ignoredWidth = image.Width; }
            catch (ArgumentException) { disposed = true; }
            AssertTrue(disposed, message);
        }

        private delegate void TestAction();

        private static void RunTest(string name, TestAction action, ref int failureCount)
        {
            try
            {
                action();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  [PASS] " + name);
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  [FAIL] " + name + ": " + ex.Message);
                Console.ResetColor();
                failureCount++;
            }
        }

        private static void AssertEqual(int expected, int actual, string message)
        {
            if (expected != actual)
            {
                throw new Exception(string.Format("{0} (Expected {1}, got {2})", message, expected, actual));
            }
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception(message);
            }
        }
    }
}
