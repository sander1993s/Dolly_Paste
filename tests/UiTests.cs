using System;
using System.Drawing;
using System.IO;
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
                            MainForm.ClampWindowSize(mainWorkArea, new Size(420, 520), new Size(380, 420), out expectedMainInit, out expectedMainMin);

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
                        RunTest("Compact Layout and Optional Preview at Default Size (420x520)", delegate
                        {
                            form.Size = new Size(420, 520);
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
                        RunTest("Compact Layout and Optional Preview at Minimum Size (380x420)", delegate
                        {
                            if (form.DetailPanel.Visible) form.PreviewToggleButton.PerformClick();
                            form.Size = new Size(380, 420);
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
                            form.Size = new Size(420, 520);
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
                form.HeaderPanel, form.SearchFilterPanel, form.ContentPanel, form.DetailToolbarPanel
            };
            AssertNonOverlappingChildren(mainRegions, form.MainPanel, "Main region");

            Control[] searchControls = new Control[]
            {
                form.SearchTextBox, form.NavAllButton, form.NavPinnedButton,
                form.FilterAllButton, form.FilterTextButton, form.FilterLinkButton, form.FilterCodeButton
            };
            AssertNonOverlappingChildren(searchControls, form.SearchFilterPanel, "Search/filter control");

            Control[] headerControls = new Control[]
            {
                form.HeaderTitleLabel, form.PauseResumeButton, form.SettingsButton
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
                AssertFits(form.DetailMetaPanel, form.DetailContentPanel, "DetailMetaPanel");
                AssertFits(form.DetailTextContainerPanel, form.DetailContentPanel, "DetailTextContainerPanel");
                AssertNoIntersection(form.DetailMetaPanel.Bounds, form.DetailTextContainerPanel.Bounds,
                    "DetailMetaPanel", "DetailTextContainerPanel");
                Control[] detailLabels = new Control[]
                {
                    form.DetailKindLabel, form.DetailTimeLabel, form.CharCountLabel
                };
                AssertNonOverlappingChildren(detailLabels, form.DetailMetaPanel, "Preview metadata");
            }

            // Hidden toast overlays must not steal room from status or clip actions.
            AssertTrue(form.StatusLeftLabel.Right <= form.StatusRightLabel.Left,
                string.Format("StatusLeftLabel (Right={0}) must precede StatusRightLabel (Left={1})",
                    form.StatusLeftLabel.Right, form.StatusRightLabel.Left));
            AssertNoIntersection(form.StatusLeftLabel.Bounds, form.StatusRightLabel.Bounds, "StatusLeftLabel", "StatusRightLabel");
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
