using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace DollyPaste
{
    internal enum CommandLineMode
    {
        Normal,
        Demo,
        Preview,
        Invalid
    }

    internal sealed class CommandLineOptions
    {
        public CommandLineMode Mode { get; set; }
        public string PreviewOutputPath { get; set; }
        public string ErrorMessage { get; set; }
    }

    internal static class Program
    {
        private const string SingleInstanceMutexName = "Local\\DollyPaste_SingleInstance_Mutex_2026";

        internal static bool IsFullyQualifiedPngPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (char.IsWhiteSpace(path[0]) || char.IsWhiteSpace(path[path.Length - 1])) return false;
            if (path.Length < 5) return false;
            if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return false;
            if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;

            // 1. Drive-rooted fully qualified: "C:\..." or "c:/..."
            if (char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
            {
                return true;
            }

            // 2. UNC fully qualified: "\\server\share\..." or "//server/share/..."
            if ((path.StartsWith(@"\\") && !path.StartsWith(@"\\\")) ||
                (path.StartsWith("//") && !path.StartsWith("///")))
            {
                int shareSlash = path.IndexOfAny(new char[] { '\\', '/' }, 2);
                if (shareSlash > 2)
                {
                    int fileSlash = path.IndexOfAny(new char[] { '\\', '/' }, shareSlash + 1);
                    if (fileSlash > shareSlash)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal static CommandLineOptions ParseArguments(string[] args)
        {
            CommandLineOptions options = new CommandLineOptions();
            options.Mode = CommandLineMode.Normal;

            if (args == null || args.Length == 0)
            {
                return options;
            }

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.Equals(arg, "--demo", StringComparison.OrdinalIgnoreCase))
                {
                    if (options.Mode != CommandLineMode.Normal && options.Mode != CommandLineMode.Demo)
                    {
                        options.Mode = CommandLineMode.Invalid;
                        options.ErrorMessage = "Error: Conflicting command-line arguments specified.";
                        return options;
                    }
                    options.Mode = CommandLineMode.Demo;
                }
                else if (string.Equals(arg, "--preview", StringComparison.OrdinalIgnoreCase))
                {
                    if (options.Mode != CommandLineMode.Normal && options.Mode != CommandLineMode.Preview)
                    {
                        options.Mode = CommandLineMode.Invalid;
                        options.ErrorMessage = "Error: Conflicting command-line arguments specified.";
                        return options;
                    }

                    if (i + 1 >= args.Length)
                    {
                        options.Mode = CommandLineMode.Invalid;
                        options.ErrorMessage = "Error: --preview requires an absolute .png file path.";
                        return options;
                    }

                    string path = args[i + 1];
                    i++;

                    if (!IsFullyQualifiedPngPath(path))
                    {
                        options.Mode = CommandLineMode.Invalid;
                        options.ErrorMessage = "Error: --preview requires a valid rooted absolute .png file path.";
                        return options;
                    }

                    options.Mode = CommandLineMode.Preview;
                    options.PreviewOutputPath = path;
                }
                else
                {
                    options.Mode = CommandLineMode.Invalid;
                    options.ErrorMessage = string.Format("Error: Unrecognized command-line argument '{0}'.", arg);
                    return options;
                }
            }

            return options;
        }

        [STAThread]
        private static int Main(string[] args)
        {
            CommandLineOptions options = ParseArguments(args);
            if (options.Mode == CommandLineMode.Invalid)
            {
                Console.Error.WriteLine(options.ErrorMessage);
                return 1;
            }

            if (options.Mode == CommandLineMode.Preview)
            {
                return RunDemoOrPreview(false, true, options.PreviewOutputPath);
            }

            if (options.Mode == CommandLineMode.Demo)
            {
                return RunDemoOrPreview(true, false, null);
            }

            // Normal application execution with single-instance mutex enforcement
            return RunNormalMode();
        }

        private static int RunDemoOrPreview(bool isDemoMode, bool isPreviewMode, string previewOutputPath)
        {
            if (isPreviewMode)
            {
                // Delete existing output file to guarantee no stale file success
                try
                {
                    if (File.Exists(previewOutputPath))
                    {
                        File.Delete(previewOutputPath);
                    }
                }
                catch
                {
                    // Ignore deletion failure; renderSuccess check will guard this
                }
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "DollyPaste_Demo_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempDir);

                Settings settings = new Settings();
                settings.Normalize();

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                bool renderSuccess = false;
                string renderError = null;

                using (HistoryStore history = new HistoryStore(settings, tempDir))
                {
                    // Populate neutral synthetic sample clips using actual core methods:
                    // 1. Neutral shopping note (TEXT)
                    history.Capture(
                        "Shopping list:\n• Rolled oats\n• Whole milk\n• Local wildflower honey\n• Green tea",
                        1);

                    // 2. Neutral documentation URL (LINK)
                    history.Capture(
                        "https://example.com/notes",
                        2);

                    // 3. Short code snippet (CODE)
                    history.Capture(
                        "function formatTimestamp(utcDate) {\n    return new Intl.DateTimeFormat('en-US').format(utcDate);\n}",
                        3);

                    // 4. Ordinary note; automatic sensitive-content censoring has been removed.
                    history.Capture(
                        "Remember to send the meeting notes after lunch.",
                        4);

                    // Demo/preview never instantiates WindowsClipboard or touches system clipboard
                    using (MainForm form = new MainForm(history, null, tempDir, isDemoMode, isPreviewMode, previewOutputPath))
                    {
                        Application.Run(form);
                        if (isPreviewMode)
                        {
                            renderSuccess = form.PreviewRenderSucceeded;
                            renderError = form.PreviewRenderError;
                        }
                    }

                    if (isPreviewMode)
                    {
                        if (!renderSuccess)
                        {
                            Console.Error.WriteLine("Error: Preview screenshot generation failed: " + (renderError ?? "Unknown render failure"));
                            return 2;
                        }

                        if (!File.Exists(previewOutputPath))
                        {
                            Console.Error.WriteLine("Error: Preview screenshot file was not generated.");
                            return 2;
                        }

                        FileInfo fi = new FileInfo(previewOutputPath);
                        if (fi.Length == 0)
                        {
                            Console.Error.WriteLine("Error: Generated preview screenshot is empty.");
                            return 3;
                        }
                    }
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error in demo/preview execution: " + ex.Message);
                return 4;
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
                    // Ignore transient cleanup errors in temp folder
                }
            }
        }

        private static int RunNormalMode()
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, SingleInstanceMutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // Existing instance running: signal it to bring window to front
                    uint msg = MainForm.RegisterWindowMessage(MainForm.ShowMessageName);
                    MainForm.PostMessage(MainForm.HWND_BROADCAST, msg, IntPtr.Zero, IntPtr.Zero);
                    return 0;
                }

                string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string dataDir = Path.Combine(appData, "DollyPaste");
                if (!Directory.Exists(dataDir))
                {
                    Directory.CreateDirectory(dataDir);
                }

                Settings settings = SettingsStore.Load(dataDir);
                if (settings == null)
                {
                    settings = new Settings();
                    settings.Normalize();
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                using (HistoryStore history = new HistoryStore(settings, dataDir))
                {
                    MainForm form = null;
                    WindowsClipboard clipboard = null;
                    try
                    {
                        form = new MainForm(history, null, dataDir, false, false, null);

                        clipboard = CreateClipboard(form, history, false);

                        form.SetClipboard(clipboard);
                        Application.Run(form);
                    }
                    finally
                    {
                        if (clipboard != null)
                        {
                            clipboard.Dispose();
                        }
                        if (form != null)
                        {
                            form.Dispose();
                        }
                    }
                }
                return 0;
            }
        }

        internal static WindowsClipboard CreateClipboard(MainForm form, HistoryStore history, bool startPaused)
        {
            return new WindowsClipboard(
                form,
                delegate(string text, uint sequence) { history.Capture(text, sequence); },
                delegate { form.ToggleVisibility(); },
                startPaused);
        }
    }
}
