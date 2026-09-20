using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

namespace DollyPaste
{
    public sealed class WindowsClipboard : IDisposable
    {
        #region Win32 Constants and P/Invoke

        private const uint GMEM_MOVEABLE = 0x0002;
        private const uint CF_UNICODETEXT = 13;

        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_NOREPEAT = 0x4000;

        private const int VK_SHIFT = 0x10;
        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12; // Alt
        private const int VK_INSERT = 0x2D;
        private const int VK_V = 0x56;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        private const int GWL_STYLE = -16;
        private const int ES_PASSWORD = 0x0020;
        private const uint EM_GETPASSWORDCHAR = 0x00D2;
        private const uint SMTO_ABORTIFHUNG = 0x0002;
        private const uint SMTO_BLOCK = 0x0001;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GUITHREADINFO
        {
            public int cbSize;
            public uint flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public RECT rcCaret;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern uint GetClipboardSequenceNumber();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("user32.dll")]
        private static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint RegisterClipboardFormat(string lpszFormat);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern UIntPtr GlobalSize(IntPtr hMem);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        #endregion

        #region Registered Clipboard Format IDs

        internal static readonly uint FormatExcludeFromMonitor = RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
        internal static readonly uint FormatCanIncludeInHistory = RegisterClipboardFormat("CanIncludeInClipboardHistory");
        internal static readonly uint FormatCanUploadToCloud = RegisterClipboardFormat("CanUploadToCloudClipboard");

        #endregion

        #region Fields

        private readonly Form _owner;
        private readonly Action<string, uint> _onCapture;
        private readonly Action<uint> _onProtect;
        private readonly Action _onToggle;
        private readonly Func<bool> _protectUnknown;

        private volatile bool _paused;
        private volatile bool _disposed;
        private volatile bool _cachedProtectUnknown;

        private volatile uint _lastCopiedSequence;
        private volatile uint _currentTrackedSequence;

        private ClipboardListenerWindow _listenerWindow;

        // Keep low-level hook delegate rooted to prevent GC collection
        private readonly LowLevelKeyboardProc _hookProc;
        private IntPtr _hookHandle;

        private readonly Thread _uiaThread;
        private readonly AutoResetEvent _uiaSignal;
        private readonly Queue<UiaWorkItem> _uiaQueue;
        private readonly object _uiaLock;
        private volatile bool _uiaStopping;
        private volatile bool _uiaSubscribed;

        private string _hookError;
        private string _hotkeyError;
        private string _listenerError;
        private string _uiaError;

        // Coordinators
        private readonly ReaderCoordinator _readerCoordinator;
        private readonly PasteCoordinator _pasteCoordinator;

        // Hook asynchronous posting staging queue
        private readonly List<RawPasteEvent> _hookStagingQueue;
        private readonly object _hookStagingLock;
        private int _hookPostPending;

        // Single bounded native probe worker
        private readonly Thread _nativeThread;
        private readonly AutoResetEvent _nativeSignal;
        private readonly Queue<PasteRecord> _nativeQueue;
        private readonly object _nativeLock;
        private volatile bool _nativeStopping;

        // Shared deadline timer
        private System.Threading.Timer _sharedDeadlineTimer;


        #endregion

        #region Constructor & Disposal

        public WindowsClipboard(
            Form owner,
            Action<string, uint> onCapture,
            Action onToggle)
            : this(owner, onCapture, onToggle, false)
        {
        }

        // Starting paused permits verifying the production listener without reading clipboard payloads.
        internal WindowsClipboard(
            Form owner,
            Action<string, uint> onCapture,
            Action onToggle,
            bool startPaused)
            : this(owner, onCapture, null, onToggle, null, startPaused)
        {
        }

        private WindowsClipboard(
            Form owner,
            Action<string, uint> onCapture,
            Action<uint> onProtect,
            Action onToggle,
            Func<bool> protectUnknown,
            bool startPaused)
        {
            if (owner == null)
            {
                throw new ArgumentNullException("owner");
            }

            _owner = owner;
            _onCapture = onCapture;
            _onProtect = onProtect;
            _onToggle = onToggle;
            _protectUnknown = protectUnknown;
            _cachedProtectUnknown = true; // safe default

            if (_protectUnknown != null)
            {
                try
                {
                    _cachedProtectUnknown = _protectUnknown();
                }
                catch
                {
                    _cachedProtectUnknown = true;
                }
            }

            _paused = startPaused;
            _disposed = false;
            _lastCopiedSequence = 0;
            _currentTrackedSequence = GetClipboardSequenceNumber();

            _readerCoordinator = new ReaderCoordinator();
            _pasteCoordinator = new PasteCoordinator(32);

            _hookStagingQueue = new List<RawPasteEvent>();
            _hookStagingLock = new object();
            _hookPostPending = 0;

            _nativeSignal = onProtect != null ? new AutoResetEvent(false) : null;
            _nativeQueue = new Queue<PasteRecord>();
            _nativeLock = new object();
            _nativeStopping = false;

            _uiaSignal = onProtect != null ? new AutoResetEvent(false) : null;
            _uiaQueue = new Queue<UiaWorkItem>();
            _uiaLock = new object();
            _uiaStopping = false;

            // Set up clipboard format listener and hotkey window
            _listenerWindow = new ClipboardListenerWindow(this);
            _owner.HandleCreated += OnOwnerHandleCreated;
            _owner.HandleDestroyed += OnOwnerHandleDestroyed;

            if (_owner.IsHandleCreated)
            {
                _listenerWindow.Attach(_owner.Handle);
            }
            else
            {
                // Force handle creation on UI thread
                IntPtr forcedHandle = _owner.Handle;
                _listenerWindow.Attach(forcedHandle);
            }

            // The application no longer censors clips based on paste destinations or password fields.
            // Clipboard capture and the global popup shortcut are handled by the listener above.
            if (_onProtect == null) return;

            // Legacy detection machinery is retained for existing regression coverage only.
            // Install low-level keyboard hook
            _hookProc = new LowLevelKeyboardProc(HookCallback);
            InstallKeyboardHook();

            // Start dedicated background worker thread for native probes
            _nativeThread = new Thread(new ThreadStart(NativeWorkerLoop));
            _nativeThread.SetApartmentState(ApartmentState.MTA);
            _nativeThread.IsBackground = true;
            _nativeThread.Name = "DollyPaste-NativeWorker";
            _nativeThread.Start();

            // Start dedicated background MTA thread for UI Automation
            _uiaThread = new Thread(new ThreadStart(UiaWorkerLoop));
            _uiaThread.SetApartmentState(ApartmentState.MTA);
            _uiaThread.IsBackground = true; // Never prevent fast process exit
            _uiaThread.Name = "DollyPaste-UIA-MTA";
            _uiaThread.Start();

            // Single shared deadline timer for bounded paste fallback (ticks every 100ms)
            _sharedDeadlineTimer = new System.Threading.Timer(new TimerCallback(OnDeadlineTimerTick), null, 100, 100);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            // Remove form event subscriptions
            if (_owner != null)
            {
                try
                {
                    _owner.HandleCreated -= OnOwnerHandleCreated;
                    _owner.HandleDestroyed -= OnOwnerHandleDestroyed;
                }
                catch
                {
                }
            }

            // Detach listener window
            if (_listenerWindow != null)
            {
                try
                {
                    _listenerWindow.Dispose();
                }
                catch
                {
                }
                _listenerWindow = null;
            }

            // Uninstall keyboard hook
            UninstallKeyboardHook();

            // Dispose shared deadline timer
            if (_sharedDeadlineTimer != null)
            {
                try
                {
                    _sharedDeadlineTimer.Dispose();
                }
                catch
                {
                }
                _sharedDeadlineTimer = null;
            }

            // Stop native worker thread
            _nativeStopping = true;
            try
            {
                if (_nativeSignal != null)
                {
                    _nativeSignal.Set();
                }
            }
            catch
            {
            }

            if (_nativeThread != null && _nativeThread.IsAlive)
            {
                try
                {
                    _nativeThread.Join(100);
                }
                catch
                {
                }
            }

            // Stop MTA UIA thread
            _uiaStopping = true;
            try
            {
                if (_uiaSignal != null)
                {
                    _uiaSignal.Set();
                }
            }
            catch
            {
            }

            // Bounded wait for MTA thread; background thread will terminate on exit anyway
            if (_uiaThread != null && _uiaThread.IsAlive)
            {
                try
                {
                    _uiaThread.Join(100);
                }
                catch
                {
                }
            }

            // Clear queues & coordinators
            lock (_nativeLock)
            {
                _nativeQueue.Clear();
            }

            lock (_uiaLock)
            {
                _uiaQueue.Clear();
            }

            lock (_hookStagingLock)
            {
                _hookStagingQueue.Clear();
            }

            _pasteCoordinator.Clear();
            _readerCoordinator.Reset();
        }

        #endregion

        #region Public Properties

        public bool Paused
        {
            get
            {
                return _paused;
            }
            set
            {
                _paused = value;
            }
        }

        public string Status
        {
            get
            {
                if (_disposed)
                {
                    return "Disposed";
                }

                List<string> issues = new List<string>();

                if (_listenerWindow == null || !_listenerWindow.ListenerRegistered)
                {
                    issues.Add(!string.IsNullOrEmpty(_listenerError) ? _listenerError : "Clipboard listener unavailable");
                }

                if (_onProtect != null && _hookHandle == IntPtr.Zero)
                {
                    issues.Add(!string.IsNullOrEmpty(_hookError) ? _hookError : "Keyboard hook unavailable");
                }

                if (_listenerWindow == null || !_listenerWindow.HotkeyRegistered)
                {
                    issues.Add(!string.IsNullOrEmpty(_hotkeyError) ? _hotkeyError : "Hotkey Ctrl+Shift+V unavailable");
                }

                if (_onProtect != null && !_uiaSubscribed)
                {
                    issues.Add(!string.IsNullOrEmpty(_uiaError) ? _uiaError : "UI Automation unavailable");
                }

                string baseStatus = _paused ? "Paused" : "Active";
                if (issues.Count == 0)
                {
                    return baseStatus;
                }

                return baseStatus + " (" + string.Join(", ", issues.ToArray()) + ")";
            }
        }

        #endregion

        #region Public Methods

        public bool CopyText(string text, Action<uint> onCopied)
        {
            if (text == null || _disposed)
            {
                return false;
            }

            IntPtr ownerHwnd = IntPtr.Zero;
            if (_owner != null && !_owner.IsDisposed && _owner.IsHandleCreated)
            {
                ownerHwnd = _owner.Handle;
            }

            uint publishedSequence;
            bool success = ExecutePublication(Win32PublicationNative.Instance, ownerHwnd, text, out publishedSequence);
            if (!success)
            {
                return false;
            }

            _lastCopiedSequence = publishedSequence;
            _currentTrackedSequence = publishedSequence;

            if (onCopied != null)
            {
                SafeInvokeOnUI(delegate
                {
                    if (!_disposed)
                    {
                        onCopied(publishedSequence);
                    }
                });
            }

            return true;
        }

        #endregion

        #region Publication State Machine & Native Abstraction

        internal interface IClipboardPublicationNative
        {
            uint RegisterClipboardFormat(string formatName);
            IntPtr AllocGlobal(uint bytes);
            void FreeGlobal(IntPtr hMem);
            IntPtr LockGlobal(IntPtr hMem);
            bool UnlockGlobal(IntPtr hMem);
            bool OpenClipboard(IntPtr hwnd);
            bool EmptyClipboard();
            IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
            uint GetClipboardSequenceNumber();
            bool CloseClipboard();
        }

        internal sealed class Win32PublicationNative : IClipboardPublicationNative
        {
            public static readonly Win32PublicationNative Instance = new Win32PublicationNative();

            public uint RegisterClipboardFormat(string formatName)
            {
                return WindowsClipboard.RegisterClipboardFormat(formatName);
            }

            public IntPtr AllocGlobal(uint bytes)
            {
                return WindowsClipboard.GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
            }

            public void FreeGlobal(IntPtr hMem)
            {
                WindowsClipboard.GlobalFree(hMem);
            }

            public IntPtr LockGlobal(IntPtr hMem)
            {
                return WindowsClipboard.GlobalLock(hMem);
            }

            public bool UnlockGlobal(IntPtr hMem)
            {
                return WindowsClipboard.GlobalUnlock(hMem);
            }

            public bool OpenClipboard(IntPtr hwnd)
            {
                return WindowsClipboard.OpenClipboard(hwnd);
            }

            public bool EmptyClipboard()
            {
                return WindowsClipboard.EmptyClipboard();
            }

            public IntPtr SetClipboardData(uint uFormat, IntPtr hMem)
            {
                return WindowsClipboard.SetClipboardData(uFormat, hMem);
            }

            public uint GetClipboardSequenceNumber()
            {
                return WindowsClipboard.GetClipboardSequenceNumber();
            }

            public bool CloseClipboard()
            {
                return WindowsClipboard.CloseClipboard();
            }
        }

        internal static bool ExecutePublication(
            IClipboardPublicationNative native,
            IntPtr ownerHwnd,
            string text,
            out uint publishedSequence)
        {
            publishedSequence = 0;
            if (native == null || text == null)
            {
                return false;
            }

            uint cfHistory = native.RegisterClipboardFormat("CanIncludeInClipboardHistory");
            uint cfCloud = native.RegisterClipboardFormat("CanUploadToCloudClipboard");
            if (cfHistory == 0 || cfCloud == 0)
            {
                return false;
            }

            // Allocate all buffers before Open/EmptyClipboard to prevent losing user clipboard if allocation fails
            IntPtr hHistory = IntPtr.Zero;
            IntPtr hCloud = IntPtr.Zero;
            IntPtr hText = IntPtr.Zero;

            try
            {
                // 1. CanIncludeInClipboardHistory = 0 (DWORD)
                hHistory = native.AllocGlobal(4);
                if (hHistory == IntPtr.Zero)
                {
                    FreePublicationBuffers(native, ref hHistory, ref hCloud, ref hText);
                    return false;
                }
                IntPtr pHistory = native.LockGlobal(hHistory);
                if (pHistory == IntPtr.Zero)
                {
                    FreePublicationBuffers(native, ref hHistory, ref hCloud, ref hText);
                    return false;
                }
                Marshal.WriteInt32(pHistory, 0);
                native.UnlockGlobal(hHistory);

                // 2. CanUploadToCloudClipboard = 0 (DWORD)
                hCloud = native.AllocGlobal(4);
                if (hCloud == IntPtr.Zero)
                {
                    FreePublicationBuffers(native, ref hHistory, ref hCloud, ref hText);
                    return false;
                }
                IntPtr pCloud = native.LockGlobal(hCloud);
                if (pCloud == IntPtr.Zero)
                {
                    FreePublicationBuffers(native, ref hHistory, ref hCloud, ref hText);
                    return false;
                }
                Marshal.WriteInt32(pCloud, 0);
                native.UnlockGlobal(hCloud);

                // 3. CF_UNICODETEXT buffer
                byte[] textBytes = Encoding.Unicode.GetBytes(text + "\0");
                hText = native.AllocGlobal((uint)textBytes.Length);
                if (hText == IntPtr.Zero)
                {
                    FreePublicationBuffers(native, ref hHistory, ref hCloud, ref hText);
                    return false;
                }
                IntPtr pText = native.LockGlobal(hText);
                if (pText == IntPtr.Zero)
                {
                    FreePublicationBuffers(native, ref hHistory, ref hCloud, ref hText);
                    return false;
                }
                Marshal.Copy(textBytes, 0, pText, textBytes.Length);
                native.UnlockGlobal(hText);
            }
            catch
            {
                FreePublicationBuffers(native, ref hHistory, ref hCloud, ref hText);
                return false;
            }

            // Bounded retry to open clipboard without sleeping
            bool opened = false;
            for (int retry = 0; retry < 5; retry++)
            {
                if (native.OpenClipboard(ownerHwnd))
                {
                    opened = true;
                    break;
                }
            }

            if (!opened)
            {
                FreePublicationBuffers(native, ref hHistory, ref hCloud, ref hText);
                return false;
            }

            try
            {
                if (!native.EmptyClipboard())
                {
                    FreePublicationBuffers(native, ref hHistory, ref hCloud, ref hText);
                    return false;
                }

                // Win32 Ownership Rule:
                // If SetClipboardData succeeds, system takes ownership (set local pointer to Zero).
                // If SetClipboardData fails, caller still owns and MUST free.
                // Publish CanIncludeInClipboardHistory and CanUploadToCloudClipboard BEFORE text.
                // Do not publish text after a privacy format failure.

                IntPtr resHistory = native.SetClipboardData(cfHistory, hHistory);
                if (resHistory == IntPtr.Zero)
                {
                    FreePublicationBuffers(native, ref hHistory, ref hCloud, ref hText);
                    return false;
                }
                hHistory = IntPtr.Zero; // System owns it now

                IntPtr resCloud = native.SetClipboardData(cfCloud, hCloud);
                if (resCloud == IntPtr.Zero)
                {
                    FreePublicationBuffers(native, ref hHistory, ref hCloud, ref hText);
                    return false;
                }
                hCloud = IntPtr.Zero; // System owns it now

                IntPtr resText = native.SetClipboardData(CF_UNICODETEXT, hText);
                if (resText == IntPtr.Zero)
                {
                    FreePublicationBuffers(native, ref hHistory, ref hCloud, ref hText);
                    return false;
                }
                hText = IntPtr.Zero; // System owns it now

                // Capture exact sequence while clipboard remains open and owned!
                publishedSequence = native.GetClipboardSequenceNumber();

                native.CloseClipboard();
                opened = false;
                return true;
            }
            finally
            {
                if (opened)
                {
                    native.CloseClipboard();
                }
                FreePublicationBuffers(native, ref hHistory, ref hCloud, ref hText);
            }
        }

        private static void FreePublicationBuffers(IClipboardPublicationNative native, ref IntPtr h1, ref IntPtr h2, ref IntPtr h3)
        {
            if (native != null)
            {
                if (h1 != IntPtr.Zero)
                {
                    native.FreeGlobal(h1);
                    h1 = IntPtr.Zero;
                }
                if (h2 != IntPtr.Zero)
                {
                    native.FreeGlobal(h2);
                    h2 = IntPtr.Zero;
                }
                if (h3 != IntPtr.Zero)
                {
                    native.FreeGlobal(h3);
                    h3 = IntPtr.Zero;
                }
            }
        }

        #endregion

        #region Internal Helper & Test APIs

        internal static bool IsRecognizedEditOrRichEditClass(string className)
        {
            if (string.IsNullOrEmpty(className))
            {
                return false;
            }

            // Standard Win32 Edit
            if (string.Equals(className, "Edit", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // WinForms TextBox controls use WindowsForms10.EDIT.*
            if (className.StartsWith("WindowsForms10.EDIT.", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Known anchored RichEdit variants (no arbitrary substring matching)
            if (string.Equals(className, "RICHEDIT", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(className, "RichEdit20A", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(className, "RichEdit20W", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(className, "RichEdit50W", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(className, "RichEdit60W", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(className, "MsftEdit_v1.0", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // WinForms RichTextBox controls use WindowsForms10.RichEdit*
            if (className.StartsWith("WindowsForms10.RichEdit", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        internal static bool? ProbeNativePasswordControl(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
            {
                return null;
            }

            StringBuilder sb = new StringBuilder(256);
            int len = GetClassName(hwnd, sb, sb.Capacity);
            if (len == 0)
            {
                return null;
            }

            string className = sb.ToString();
            if (!IsRecognizedEditOrRichEditClass(className))
            {
                return null; // Not a recognized native edit control
            }

            // Check ES_PASSWORD in window style
            int style = GetWindowLong(hwnd, GWL_STYLE);
            if ((style & ES_PASSWORD) != 0)
            {
                return true;
            }

            // Query password character via bounded SendMessageTimeout
            IntPtr passwordChar;
            IntPtr smResult = SendMessageTimeout(
                hwnd,
                EM_GETPASSWORDCHAR,
                IntPtr.Zero,
                IntPtr.Zero,
                SMTO_ABORTIFHUNG | SMTO_BLOCK,
                100,
                out passwordChar);

            if (smResult != IntPtr.Zero)
            {
                if (passwordChar.ToInt64() != 0)
                {
                    return true;
                }
                return false; // Recognized ordinary edit control verified
            }

            return null; // Could not verify via SendMessageTimeout
        }

        internal static bool TryParseUnicodeTextFromBuffer(IntPtr pText, ulong byteLength, out string result)
        {
            result = null;
            if (pText == IntPtr.Zero || byteLength < 2 || (byteLength % 2) != 0)
            {
                return false;
            }

            ulong totalChars = byteLength / 2;
            int charsToScan = (int)Math.Min(totalChars, (ulong)32769);
            int termIndex = -1;
            for (int i = 0; i < charsToScan; i++)
            {
                char c = (char)Marshal.ReadInt16(pText, i * 2);
                if (c == '\0')
                {
                    termIndex = i;
                    break;
                }
            }

            // Require null terminator within 32768 chars; reject empty or oversized
            if (termIndex <= 0 || termIndex > 32768)
            {
                return false;
            }

            result = Marshal.PtrToStringUni(pText, termIndex);
            return true;
        }

        internal static bool TryReadHistoryDword(IntPtr pHist, ulong byteLength, out uint dword)
        {
            dword = 0;
            if (pHist == IntPtr.Zero || byteLength < 4)
            {
                return false;
            }
            dword = (uint)Marshal.ReadInt32(pHist);
            return true;
        }

        internal static bool ShouldSkipCapturePayload(
            bool hasExcludeMonitorFormat,
            bool hasHistoryFormat,
            uint historyFormatDword,
            bool hasUnicodeText,
            int textLengthChars)
        {
            if (hasExcludeMonitorFormat)
            {
                return true;
            }

            if (hasHistoryFormat && historyFormatDword == 0)
            {
                return true;
            }

            if (!hasUnicodeText)
            {
                return true;
            }

            if (textLengthChars <= 0 || textLengthChars > 32768)
            {
                return true;
            }

            return false;
        }

        internal static bool CanPublishCopy(
            bool historyFormatRegistered,
            bool cloudFormatRegistered,
            bool historyAllocated,
            bool cloudAllocated,
            bool textAllocated)
        {
            if (!historyFormatRegistered || !cloudFormatRegistered)
            {
                return false;
            }
            if (!historyAllocated || !cloudAllocated || !textAllocated)
            {
                return false;
            }
            return true;
        }

        internal enum DestinationClassification
        {
            Password,
            OrdinaryEdit,
            Unknown
        }

        internal static DestinationClassification ClassifyDestinationMetadata(
            bool? nativeProbe,
            bool focusMatchesContext,
            bool uiaIsPassword,
            bool uiaIsSupported,
            bool uiaIsEditControl)
        {
            if (nativeProbe.HasValue)
            {
                if (nativeProbe.Value)
                {
                    return DestinationClassification.Password;
                }
                if (focusMatchesContext)
                {
                    return DestinationClassification.OrdinaryEdit;
                }
            }

            if (!focusMatchesContext)
            {
                return DestinationClassification.Unknown;
            }

            if (uiaIsPassword)
            {
                return DestinationClassification.Password;
            }

            // Only actual identified editable element with supported Boolean IsPassword=false is OrdinaryEdit
            if (uiaIsSupported && uiaIsEditControl)
            {
                return DestinationClassification.OrdinaryEdit;
            }

            // Document/container/root or unsupported IsPassword remains Unknown
            return DestinationClassification.Unknown;
        }

        internal static DestinationClassification ClassifyDestinationMetadata(
            bool? nativeProbe,
            bool focusMatchesContext,
            bool uiaIsPassword,
            bool uiaIsEditControl)
        {
            return ClassifyDestinationMetadata(nativeProbe, focusMatchesContext, uiaIsPassword, true, uiaIsEditControl);
        }

        internal static bool ShouldProtectOnPaste(
            DestinationClassification classification,
            bool protectUnknown)
        {
            if (classification == DestinationClassification.Password)
            {
                return true;
            }
            if (classification == DestinationClassification.Unknown && protectUnknown)
            {
                return true;
            }
            return false;
        }

        internal static bool IsEligibleOrdinaryEdit(
            IntPtr elementHwnd,
            IntPtr expectedFocusedHwnd,
            bool isPasswordSupported,
            bool isPassword,
            ControlType controlType,
            bool hasKeyboardFocus)
        {
            if (elementHwnd == IntPtr.Zero || expectedFocusedHwnd == IntPtr.Zero)
            {
                return false;
            }
            if (elementHwnd != expectedFocusedHwnd && (elementHwnd.ToInt64() & 0xFFFFFFFFL) != (expectedFocusedHwnd.ToInt64() & 0xFFFFFFFFL))
            {
                return false;
            }
            if (!isPasswordSupported || isPassword)
            {
                return false;
            }
            if (controlType == null || controlType != ControlType.Edit)
            {
                return false;
            }
            if (!hasKeyboardFocus)
            {
                return false;
            }
            return true;
        }

        internal static bool IsEligibleOrdinary(
            IntPtr elementHwnd,
            IntPtr expectedFocusedHwnd,
            bool isPasswordSupported,
            bool isPassword,
            ControlType controlType,
            bool hasKeyboardFocus)
        {
            return IsEligibleOrdinaryEdit(elementHwnd, expectedFocusedHwnd, isPasswordSupported, isPassword, controlType, hasKeyboardFocus);
        }

        internal static bool IsEligibleOrdinaryEdit(AutomationElement element, IntPtr expectedFocusedHwnd)
        {
            if (element == null || expectedFocusedHwnd == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                int elHwnd = 0;
                try
                {
                    elHwnd = element.Current.NativeWindowHandle;
                }
                catch
                {
                    return false;
                }

                bool isPasswordSupported = false;
                bool isPassword = false;
                try
                {
                    object passObj = element.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty, true);
                    if (passObj is bool)
                    {
                        isPasswordSupported = true;
                        isPassword = (bool)passObj;
                    }
                }
                catch
                {
                    isPasswordSupported = false;
                    isPassword = false;
                }

                ControlType ct = null;
                try
                {
                    object ctrlObj = element.GetCurrentPropertyValue(AutomationElement.ControlTypeProperty, true);
                    ct = ctrlObj as ControlType;
                }
                catch
                {
                    ct = null;
                }

                bool hasKeyboardFocus = false;
                try
                {
                    object focusObj = element.GetCurrentPropertyValue(AutomationElement.HasKeyboardFocusProperty, true);
                    if (focusObj is bool)
                    {
                        hasKeyboardFocus = (bool)focusObj;
                    }
                }
                catch
                {
                    hasKeyboardFocus = false;
                }

                return IsEligibleOrdinaryEdit(
                    (IntPtr)elHwnd,
                    expectedFocusedHwnd,
                    isPasswordSupported,
                    isPassword,
                    ct,
                    hasKeyboardFocus);
            }
            catch
            {
                return false;
            }
        }

        internal sealed class ReaderCoordinator
        {
            private readonly object _lock = new object();
            private bool _isRunning;
            private uint _latestPendingSequence;
            private int _activeWorkerCount;

            public bool IsRunning
            {
                get
                {
                    lock (_lock)
                    {
                        return _isRunning;
                    }
                }
            }

            public uint LatestPendingSequence
            {
                get
                {
                    lock (_lock)
                    {
                        return _latestPendingSequence;
                    }
                }
            }

            public int ActiveWorkerCount
            {
                get
                {
                    return _activeWorkerCount;
                }
            }

            public bool RequestRead(uint sequence)
            {
                lock (_lock)
                {
                    _latestPendingSequence = sequence;
                    if (!_isRunning)
                    {
                        _isRunning = true;
                        return true;
                    }
                    return false;
                }
            }

            public uint TryGetNextSequence()
            {
                lock (_lock)
                {
                    uint seq = _latestPendingSequence;
                    _latestPendingSequence = 0;
                    return seq;
                }
            }

            public void OnWorkerStarted()
            {
                Interlocked.Increment(ref _activeWorkerCount);
            }

            public bool OnWorkerExiting(bool canScheduleSuccessor)
            {
                Interlocked.Decrement(ref _activeWorkerCount);
                lock (_lock)
                {
                    if (canScheduleSuccessor && _latestPendingSequence != 0)
                    {
                        _isRunning = true;
                        return true;
                    }
                    else
                    {
                        _isRunning = false;
                        return false;
                    }
                }
            }

            public void Reset()
            {
                lock (_lock)
                {
                    _isRunning = false;
                    _latestPendingSequence = 0;
                }
            }
        }

        internal sealed class SchedulingCoordinator
        {
            private uint _currentSequence;
            private readonly List<uint> _protectedSequences;
            private readonly List<KeyValuePair<string, uint>> _capturedPayloads;
            private readonly ReaderCoordinator _readerCoordinator;

            public SchedulingCoordinator()
            {
                _currentSequence = 0;
                _protectedSequences = new List<uint>();
                _capturedPayloads = new List<KeyValuePair<string, uint>>();
                _readerCoordinator = new ReaderCoordinator();
            }

            public uint CurrentSequence
            {
                get { return _currentSequence; }
            }

            public List<uint> ProtectedSequences
            {
                get { return _protectedSequences; }
            }

            public List<KeyValuePair<string, uint>> CapturedPayloads
            {
                get { return _capturedPayloads; }
            }

            public ReaderCoordinator Reader
            {
                get { return _readerCoordinator; }
            }

            public void OnClipboardUpdated(uint newSequence)
            {
                _currentSequence = newSequence;
                _readerCoordinator.RequestRead(newSequence);
            }

            public bool CompleteRead(uint expectedSequence, string text)
            {
                // Completing an older read does not overwrite current sequence
                if (text != null)
                {
                    _capturedPayloads.Add(new KeyValuePair<string, uint>(text, expectedSequence));
                    return true;
                }
                return false;
            }

            public void CompleteFocusCheck(uint sequenceAtScheduleTime, bool isPassword)
            {
                if (isPassword)
                {
                    _protectedSequences.Add(sequenceAtScheduleTime);
                }
            }
        }

        #endregion

        #region Private Helper Methods

        private void SafeInvokeOnUI(Action action)
        {
            if (_disposed || action == null)
            {
                return;
            }

            try
            {
                if (_owner != null && !_owner.IsDisposed && _owner.IsHandleCreated)
                {
                    if (_owner.InvokeRequired)
                    {
                        _owner.BeginInvoke(new Action(delegate
                        {
                            if (!_disposed)
                            {
                                try
                                {
                                    action();
                                }
                                catch
                                {
                                }
                            }
                        }));
                    }
                    else
                    {
                        action();
                    }
                }
            }
            catch
            {
            }
        }

        private void RefreshCachedProtectUnknown()
        {
            if (_protectUnknown != null)
            {
                try
                {
                    _cachedProtectUnknown = _protectUnknown();
                }
                catch
                {
                }
            }
        }

        private void DispatchCapture(string text, uint sequence)
        {
            if (_disposed || _paused)
            {
                return;
            }

            SafeInvokeOnUI(delegate
            {
                if (_disposed || _paused)
                {
                    return;
                }
                if (_onCapture != null)
                {
                    _onCapture(text, sequence);
                }
            });
        }

        private void DispatchProtect(uint sequence)
        {
            if (_disposed || _onProtect == null)
            {
                return;
            }

            // Protect conservative calls are dispatched even while Paused
            SafeInvokeOnUI(delegate
            {
                if (_disposed)
                {
                    return;
                }
                if (_onProtect != null)
                {
                    _onProtect(sequence);
                }
            });
        }

        private void DispatchToggle()
        {
            if (_disposed)
            {
                return;
            }

            SafeInvokeOnUI(delegate
            {
                if (_disposed)
                {
                    return;
                }
                if (_onToggle != null)
                {
                    _onToggle();
                }
            });
        }

        private void OnOwnerHandleCreated(object sender, EventArgs e)
        {
            if (!_disposed && _listenerWindow != null && _owner != null && _owner.IsHandleCreated)
            {
                _listenerWindow.Attach(_owner.Handle);
            }
        }

        private void OnOwnerHandleDestroyed(object sender, EventArgs e)
        {
            if (_listenerWindow != null)
            {
                _listenerWindow.Detach();
            }
        }

        #endregion

        #region Native Clipboard Update & Coalesced Async Reader

        private void OnClipboardUpdated()
        {
            if (_disposed)
            {
                return;
            }

            uint seq = GetClipboardSequenceNumber();
            if (seq == _lastCopiedSequence)
            {
                // Self-copy recapture avoidance
                return;
            }

            _currentTrackedSequence = seq;

            // If paused, skip reading payload into history
            if (_paused)
            {
                return;
            }

            // Atomic request to reader coordinator: exactly one active reader, pending sequence tracked
            if (_readerCoordinator.RequestRead(seq))
            {
                ThreadPool.QueueUserWorkItem(new WaitCallback(CoalescedReadClipboardLoop));
            }
        }

        private void CoalescedReadClipboardLoop(object state)
        {
            _readerCoordinator.OnWorkerStarted();
            try
            {
                while (!_disposed && !_paused)
                {
                    uint seqToRead = _readerCoordinator.TryGetNextSequence();
                    if (seqToRead == 0)
                    {
                        break;
                    }

                    ReadClipboardPayload(seqToRead);
                }
            }
            finally
            {
                // Atomically releases running ownership with EXACTLY ONE release per worker lifetime.
                // If pending work arrived, reserves running again and returns true to schedule successor.
                bool scheduleSuccessor = _readerCoordinator.OnWorkerExiting(!_disposed && !_paused);
                if (scheduleSuccessor)
                {
                    ThreadPool.QueueUserWorkItem(new WaitCallback(CoalescedReadClipboardLoop));
                }
            }
        }

        private void ReadClipboardPayload(uint expectedSeq)
        {
            if (_disposed || _paused)
            {
                return;
            }

            // Bounded retries for contention; never blocks UI
            bool opened = false;
            for (int retry = 0; retry < 5; retry++)
            {
                if (_disposed || _paused)
                {
                    return;
                }
                if (OpenClipboard(IntPtr.Zero))
                {
                    opened = true;
                    break;
                }
                Thread.Sleep(15);
            }

            if (!opened)
            {
                return;
            }

            string capturedText = null;

            try
            {
                uint seqBefore = GetClipboardSequenceNumber();
                if (seqBefore != expectedSeq)
                {
                    return; // Clipboard already advanced
                }

                // Check producer formats BEFORE reading text:
                // 1. ExcludeClipboardContentFromMonitorProcessing skips
                if (FormatExcludeFromMonitor != 0 && IsClipboardFormatAvailable(FormatExcludeFromMonitor))
                {
                    return;
                }

                // 2. CanIncludeInClipboardHistory DWORD 0 skips; fail closed on malformed metadata (< 4 bytes)
                if (FormatCanIncludeInHistory != 0 && IsClipboardFormatAvailable(FormatCanIncludeInHistory))
                {
                    IntPtr hHist = GetClipboardData(FormatCanIncludeInHistory);
                    if (hHist == IntPtr.Zero)
                    {
                        return; // fail closed
                    }

                    UIntPtr sizeHist = GlobalSize(hHist);
                    if (sizeHist.ToUInt64() < 4)
                    {
                        return; // fail closed on malformed metadata
                    }

                    IntPtr pHist = GlobalLock(hHist);
                    if (pHist == IntPtr.Zero)
                    {
                        return; // fail closed
                    }

                    uint historyDword = 0;
                    try
                    {
                        historyDword = (uint)Marshal.ReadInt32(pHist);
                    }
                    finally
                    {
                        GlobalUnlock(hHist);
                    }

                    if (historyDword == 0)
                    {
                        return; // Excluded from history
                    }
                }

                // 3. Must have CF_UNICODETEXT
                if (!IsClipboardFormatAvailable(CF_UNICODETEXT))
                {
                    return;
                }

                IntPtr hText = GetClipboardData(CF_UNICODETEXT);
                if (hText == IntPtr.Zero)
                {
                    return;
                }

                UIntPtr sizeBytes = GlobalSize(hText);
                ulong byteLen = sizeBytes.ToUInt64();
                if (byteLen < 2 || (byteLen % 2) != 0)
                {
                    return; // Validate even allocation
                }

                IntPtr pText = GlobalLock(hText);
                if (pText == IntPtr.Zero)
                {
                    return;
                }

                try
                {
                    string parsed;
                    if (TryParseUnicodeTextFromBuffer(pText, byteLen, out parsed))
                    {
                        capturedText = parsed;
                    }
                }
                finally
                {
                    GlobalUnlock(hText);
                }

                if (string.IsNullOrEmpty(capturedText))
                {
                    return;
                }

                uint seqAfter = GetClipboardSequenceNumber();
                if (seqBefore != seqAfter)
                {
                    return; // Sequence changed during read; payload torn
                }
            }
            finally
            {
                CloseClipboard();
            }

            if (!string.IsNullOrEmpty(capturedText))
            {
                // Dispatch payload with its exact captured sequence without overwriting newer current sequence
                DispatchCapture(capturedText, expectedSeq);
            }
        }

        #endregion

        #region Keyboard Hook & Paste Detection

        private void InstallKeyboardHook()
        {
            try
            {
                IntPtr hMod = IntPtr.Zero;
                try
                {
                    using (Process curProcess = Process.GetCurrentProcess())
                    using (ProcessModule curModule = curProcess.MainModule)
                    {
                        hMod = GetModuleHandle(curModule.ModuleName);
                    }
                }
                catch
                {
                    hMod = IntPtr.Zero;
                }

                if (hMod == IntPtr.Zero)
                {
                    hMod = GetModuleHandle(null);
                }

                _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, hMod, 0);
                if (_hookHandle == IntPtr.Zero)
                {
                    _hookError = "Keyboard hook unavailable";
                }
            }
            catch
            {
                _hookError = "Keyboard hook unavailable";
            }
        }

        private void UninstallKeyboardHook()
        {
            if (_hookHandle != IntPtr.Zero)
            {
                try
                {
                    UnhookWindowsHookEx(_hookHandle);
                }
                catch
                {
                }
                _hookHandle = IntPtr.Zero;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && !_disposed)
            {
                int msg = wParam.ToInt32();
                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                {
                    KBDLLHOOKSTRUCT kbd = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                    bool isPaste = false;

                    if (kbd.vkCode == VK_V)
                    {
                        bool ctrl = (GetKeyState(VK_CONTROL) & 0x8000) != 0;
                        bool alt = (GetKeyState(VK_MENU) & 0x8000) != 0;
                        bool win = ((GetKeyState(VK_LWIN) & 0x8000) != 0) || ((GetKeyState(VK_RWIN) & 0x8000) != 0);
                        bool shift = (GetKeyState(VK_SHIFT) & 0x8000) != 0;

                        // Ctrl+V without Alt, Win, or Shift (Ctrl+Shift+V is Dolly toggle hotkey)
                        if (ctrl && !alt && !win && !shift)
                        {
                            isPaste = true;
                        }
                    }
                    else if (kbd.vkCode == VK_INSERT)
                    {
                        bool shift = (GetKeyState(VK_SHIFT) & 0x8000) != 0;
                        bool ctrl = (GetKeyState(VK_CONTROL) & 0x8000) != 0;
                        bool alt = (GetKeyState(VK_MENU) & 0x8000) != 0;
                        bool win = ((GetKeyState(VK_LWIN) & 0x8000) != 0) || ((GetKeyState(VK_RWIN) & 0x8000) != 0);

                        // Shift+Insert without Ctrl, Alt, or Win
                        if (shift && !ctrl && !alt && !win)
                        {
                            isPaste = true;
                        }
                    }

                    if (isPaste)
                    {
                        // ONLY capture small immutable event metadata: sequence, foreground HWND, and focused HWND
                        uint seq = GetClipboardSequenceNumber();
                        IntPtr fgHwnd = GetForegroundWindow();
                        IntPtr focusedHwnd = fgHwnd;
                        if (fgHwnd != IntPtr.Zero)
                        {
                            uint pid;
                            uint threadId = GetWindowThreadProcessId(fgHwnd, out pid);
                            if (threadId != 0)
                            {
                                GUITHREADINFO gui = new GUITHREADINFO();
                                gui.cbSize = Marshal.SizeOf(typeof(GUITHREADINFO));
                                if (GetGUIThreadInfo(threadId, ref gui) && gui.hwndFocus != IntPtr.Zero)
                                {
                                    focusedHwnd = gui.hwndFocus;
                                }
                            }
                        }

                        EnqueueHookEvent(seq, fgHwnd, focusedHwnd, Environment.TickCount);
                    }
                }
            }

            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        private void EnqueueHookEvent(uint seq, IntPtr fgHwnd, IntPtr focusedHwnd, int tick)
        {
            if (_disposed)
            {
                return;
            }

            lock (_hookStagingLock)
            {
                // Bound and coalesce hook events so key repeat does not accumulate unbounded callbacks
                if (_hookStagingQueue.Count > 0)
                {
                    RawPasteEvent last = _hookStagingQueue[_hookStagingQueue.Count - 1];
                    if (last.Sequence == seq &&
                        last.ForegroundHwnd == fgHwnd &&
                        last.FocusedHwnd == focusedHwnd &&
                        PasteCoordinator.CalculateElapsedTicks(last.TickCount, tick) < 50)
                    {
                        return;
                    }
                }

                while (_hookStagingQueue.Count >= 16)
                {
                    _hookStagingQueue.RemoveAt(0);
                }

                _hookStagingQueue.Add(new RawPasteEvent(seq, fgHwnd, focusedHwnd, tick));
            }

            // Explicitly asynchronous posting (NEVER inline even on owner thread)
            if (Interlocked.CompareExchange(ref _hookPostPending, 1, 0) == 0)
            {
                try
                {
                    if (_owner != null && !_owner.IsDisposed && _owner.IsHandleCreated)
                    {
                        _owner.BeginInvoke(new Action(DrainHookEventsOnUI));
                    }
                    else
                    {
                        Interlocked.Exchange(ref _hookPostPending, 0);
                    }
                }
                catch
                {
                    Interlocked.Exchange(ref _hookPostPending, 0);
                }
            }
        }

        private void DrainHookEventsOnUI()
        {
            Interlocked.Exchange(ref _hookPostPending, 0);
            if (_disposed)
            {
                return;
            }

            List<RawPasteEvent> batch = new List<RawPasteEvent>();
            lock (_hookStagingLock)
            {
                batch.AddRange(_hookStagingQueue);
                _hookStagingQueue.Clear();
            }

            if (batch.Count == 0)
            {
                return;
            }

            // Snapshot current protectUnknown on UI thread
            RefreshCachedProtectUnknown();
            bool currentProtectUnknown = _cachedProtectUnknown;

            for (int i = 0; i < batch.Count; i++)
            {
                RawPasteEvent ev = batch[i];
                List<uint> overflowProtects;
                PasteRecord record = _pasteCoordinator.RegisterPaste(
                    ev.Sequence,
                    ev.ForegroundHwnd,
                    ev.FocusedHwnd,
                    ev.TickCount,
                    currentProtectUnknown,
                    out overflowProtects);

                if (overflowProtects != null && overflowProtects.Count > 0)
                {
                    for (int p = 0; p < overflowProtects.Count; p++)
                    {
                        DispatchProtect(overflowProtects[p]);
                    }
                }

                if (record != null)
                {
                    QueueToNativeWorker(record);
                }
            }
        }

        private void QueueToNativeWorker(PasteRecord record)
        {
            if (_disposed || _nativeStopping || record == null)
            {
                return;
            }

            lock (_nativeLock)
            {
                while (_nativeQueue.Count >= 16)
                {
                    PasteRecord evicted = _nativeQueue.Dequeue();
                    if (evicted != null)
                    {
                        if (_pasteCoordinator.HandleQueueEviction(evicted))
                        {
                            DispatchProtect(evicted.Sequence);
                        }
                    }
                }

                _nativeQueue.Enqueue(record);
            }

            try
            {
                _nativeSignal.Set();
            }
            catch
            {
            }
        }

        private void NativeWorkerLoop()
        {
            while (!_nativeStopping && !_disposed)
            {
                try
                {
                    _nativeSignal.WaitOne(300);
                    if (_nativeStopping || _disposed)
                    {
                        break;
                    }

                    while (true)
                    {
                        PasteRecord record = null;
                        lock (_nativeLock)
                        {
                            if (_nativeQueue.Count > 0)
                            {
                                record = _nativeQueue.Dequeue();
                            }
                        }

                        if (record == null)
                        {
                            break;
                        }

                        ProcessPasteNativeWorker(record);
                    }
                }
                catch
                {
                }
            }
        }

        private void ProcessPasteNativeWorker(PasteRecord record)
        {
            if (record == null || _disposed || record.State != PasteState.Pending)
            {
                return;
            }

            // Compare foreground and focused HWND before probe
            IntPtr curFgBefore = GetForegroundWindow();
            IntPtr curFocusBefore = GetFocusedWindowForThread(curFgBefore);
            bool contextValidBefore = (curFgBefore == record.ForegroundHwnd &&
                                       curFocusBefore == record.FocusedHwnd &&
                                       record.FocusedHwnd != IntPtr.Zero);

            bool? probeResult = null;
            if (contextValidBefore)
            {
                probeResult = ProbeNativePasswordControl(record.FocusedHwnd);
            }

            // Compare foreground and focused HWND after probe
            IntPtr curFgAfter = GetForegroundWindow();
            IntPtr curFocusAfter = GetFocusedWindowForThread(curFgAfter);
            bool contextValidAfter = (curFgAfter == record.ForegroundHwnd &&
                                      curFocusAfter == record.FocusedHwnd &&
                                      record.FocusedHwnd != IntPtr.Zero);

            bool focusMatches = contextValidBefore && contextValidAfter;

            DestinationClassification classification = ClassifyDestinationMetadata(
                probeResult,
                focusMatches,
                false,
                false,
                false);

            if (classification == DestinationClassification.Password)
            {
                if (_pasteCoordinator.ResolvePassword(record))
                {
                    DispatchProtect(record.Sequence);
                }
            }
            else if (classification == DestinationClassification.OrdinaryEdit)
            {
                // Verified ordinary edit: moves Pending to Ordinary, retires from deadlines/overflow!
                _pasteCoordinator.ResolveOrdinary(record);
            }
            else
            {
                // Unknown: queue to UIA worker for deeper metadata
                QueuePasteToMta(record);
            }
        }

        private static IntPtr GetFocusedWindowForThread(IntPtr foregroundHwnd)
        {
            if (foregroundHwnd == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            uint pid;
            uint threadId = GetWindowThreadProcessId(foregroundHwnd, out pid);
            if (threadId == 0)
            {
                return foregroundHwnd;
            }

            GUITHREADINFO gui = new GUITHREADINFO();
            gui.cbSize = Marshal.SizeOf(typeof(GUITHREADINFO));
            if (GetGUIThreadInfo(threadId, ref gui) && gui.hwndFocus != IntPtr.Zero)
            {
                return gui.hwndFocus;
            }

            return foregroundHwnd;
        }

        private void OnDeadlineTimerTick(object state)
        {
            if (_disposed)
            {
                return;
            }

            List<uint> toProtect = _pasteCoordinator.ProcessDeadlines();
            if (toProtect != null && toProtect.Count > 0)
            {
                for (int i = 0; i < toProtect.Count; i++)
                {
                    DispatchProtect(toProtect[i]);
                }
            }
        }

        #endregion

        #region UI Automation MTA Worker & Paste Resolution

        private void QueueFocusCheckToMta(uint seq)
        {
            if (_disposed || _uiaStopping || _onProtect == null)
            {
                return;
            }

            lock (_uiaLock)
            {
                if (_uiaQueue.Count < 16)
                {
                    _uiaQueue.Enqueue(new UiaWorkItem(UiaWorkItemType.FocusCheck, null, seq));
                }
            }
            try
            {
                _uiaSignal.Set();
            }
            catch
            {
            }
        }

        private void QueuePasteToMta(PasteRecord record)
        {
            if (_disposed || _uiaStopping || _onProtect == null || record == null)
            {
                return;
            }

            lock (_uiaLock)
            {
                while (_uiaQueue.Count >= 32)
                {
                    UiaWorkItem evicted = _uiaQueue.Dequeue();
                    if (evicted != null && evicted.PasteRecord != null)
                    {
                        if (_pasteCoordinator.HandleQueueEviction(evicted.PasteRecord))
                        {
                            DispatchProtect(evicted.PasteRecord.Sequence);
                        }
                    }
                }
                _uiaQueue.Enqueue(new UiaWorkItem(UiaWorkItemType.PasteResolution, record, record.Sequence));
            }
            try
            {
                _uiaSignal.Set();
            }
            catch
            {
            }
        }

        private void UiaWorkerLoop()
        {
            try
            {
                Automation.AddAutomationFocusChangedEventHandler(new AutomationFocusChangedEventHandler(OnAutomationFocusChanged));
                _uiaSubscribed = true;
                CheckCurrentFocusOnMta(GetClipboardSequenceNumber());
            }
            catch
            {
                _uiaSubscribed = false;
                _uiaError = "UI Automation unavailable";
            }

            while (!_uiaStopping && !_disposed)
            {
                try
                {
                    _uiaSignal.WaitOne(300);
                    if (_uiaStopping || _disposed)
                    {
                        break;
                    }

                    while (true)
                    {
                        UiaWorkItem item = null;
                        lock (_uiaLock)
                        {
                            if (_uiaQueue.Count > 0)
                            {
                                item = _uiaQueue.Dequeue();
                            }
                        }

                        if (item == null)
                        {
                            break;
                        }

                        if (item.Type == UiaWorkItemType.PasteResolution)
                        {
                            ProcessPasteRequest(item.PasteRecord);
                        }
                        else if (item.Type == UiaWorkItemType.FocusCheck)
                        {
                            CheckCurrentFocusOnMta(item.Sequence);
                        }
                    }
                }
                catch
                {
                }
            }

            if (_uiaSubscribed)
            {
                try
                {
                    Automation.RemoveAutomationFocusChangedEventHandler(new AutomationFocusChangedEventHandler(OnAutomationFocusChanged));
                }
                catch
                {
                }
                _uiaSubscribed = false;
            }
        }

        private void OnAutomationFocusChanged(object sender, AutomationFocusChangedEventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                // Capture native sequence number at the event
                uint seq = GetClipboardSequenceNumber();
                if (seq == 0)
                {
                    seq = _currentTrackedSequence;
                }
                if (seq == 0)
                {
                    return;
                }

                AutomationElement element = sender as AutomationElement;
                if (element == null)
                {
                    try
                    {
                        element = AutomationElement.FocusedElement;
                    }
                    catch
                    {
                        element = null;
                    }
                }

                if (element == null)
                {
                    return;
                }

                object passObj = element.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty, true);
                if (passObj is bool && (bool)passObj)
                {
                    // Conservative immediate protect on password field focus, including while Paused
                    DispatchProtect(seq);
                }
            }
            catch
            {
            }
        }

        private void CheckCurrentFocusOnMta(uint seq)
        {
            try
            {
                if (seq == 0)
                {
                    seq = GetClipboardSequenceNumber();
                    if (seq == 0)
                    {
                        seq = _currentTrackedSequence;
                    }
                }

                if (seq == 0)
                {
                    return;
                }

                AutomationElement element = AutomationElement.FocusedElement;
                if (element != null)
                {
                    object passObj = element.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty, true);
                    if (passObj is bool && (bool)passObj)
                    {
                        DispatchProtect(seq);
                    }
                }
            }
            catch
            {
            }
        }

        private void ProcessPasteRequest(PasteRecord record)
        {
            if (record == null || _disposed || record.State != PasteState.Pending)
            {
                return;
            }

            bool focusMatches = false;
            bool isPassword = false;
            bool isPasswordSupported = false;
            bool isEditControl = false;

            try
            {
                IntPtr curFgBefore = GetForegroundWindow();
                IntPtr curFocusBefore = GetFocusedWindowForThread(curFgBefore);
                if (curFgBefore == record.ForegroundHwnd &&
                    curFocusBefore == record.FocusedHwnd &&
                    record.FocusedHwnd != IntPtr.Zero)
                {
                    focusMatches = true;
                }

                if (focusMatches)
                {
                    AutomationElement element = null;
                    try
                    {
                        element = AutomationElement.FromHandle(record.FocusedHwnd);
                    }
                    catch
                    {
                        element = null;
                    }

                    if (element == null)
                    {
                        try
                        {
                            element = AutomationElement.FocusedElement;
                        }
                        catch
                        {
                            element = null;
                        }
                    }

                    if (element != null)
                    {
                        int elHwnd = 0;
                        try
                        {
                            elHwnd = element.Current.NativeWindowHandle;
                        }
                        catch
                        {
                        }

                        if (elHwnd != 0 && (IntPtr)elHwnd != record.FocusedHwnd && (IntPtr)elHwnd != record.ForegroundHwnd)
                        {
                            focusMatches = false;
                        }

                        int[] runtimeId = null;
                        try
                        {
                            runtimeId = element.GetRuntimeId();
                        }
                        catch
                        {
                            runtimeId = null;
                        }

                        // If no verifiable event element identity exists, return Unknown instead of Ordinary
                        if (runtimeId != null && runtimeId.Length > 0)
                        {
                            object passObj = element.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty, true);
                            if (passObj is bool)
                            {
                                isPasswordSupported = true;
                                isPassword = (bool)passObj;
                            }

                            object ctrlObj = element.GetCurrentPropertyValue(AutomationElement.ControlTypeProperty, true);
                            ControlType ct = ctrlObj as ControlType;

                            bool hasKeyboardFocus = false;
                            try
                            {
                                object focusObj = element.GetCurrentPropertyValue(AutomationElement.HasKeyboardFocusProperty, true);
                                if (focusObj is bool)
                                {
                                    hasKeyboardFocus = (bool)focusObj;
                                }
                            }
                            catch
                            {
                                hasKeyboardFocus = false;
                            }

                            // Require supported IsPassword=false, ControlType.Edit, HasKeyboardFocus=true,
                            // and element NONZERO nativeHWND EXACTLY EQUAL record.FocusedHwnd.
                            if (IsEligibleOrdinaryEdit((IntPtr)elHwnd, record.FocusedHwnd, isPasswordSupported, isPassword, ct, hasKeyboardFocus))
                            {
                                isEditControl = true;
                            }
                        }
                        else
                        {
                            focusMatches = false;
                        }

                        // Revalidation after provider calls
                        IntPtr curFgAfter = GetForegroundWindow();
                        IntPtr curFocusAfter = GetFocusedWindowForThread(curFgAfter);
                        if (curFgAfter != record.ForegroundHwnd || curFocusAfter != record.FocusedHwnd)
                        {
                            focusMatches = false;
                        }
                    }
                    else
                    {
                        focusMatches = false;
                    }
                }
            }
            catch
            {
                focusMatches = false;
            }

            DestinationClassification classification = ClassifyDestinationMetadata(
                null,
                focusMatches,
                isPassword,
                isPasswordSupported,
                isEditControl);

            record.UiaProcessed = 1;

            if (classification == DestinationClassification.Password)
            {
                // Confirmed password results protect captured sequence even after deadline!
                if (_pasteCoordinator.ResolvePassword(record))
                {
                    DispatchProtect(record.Sequence);
                }
            }
            else if (classification == DestinationClassification.OrdinaryEdit)
            {
                // Verified ordinary edit: moves Pending to Ordinary, retires from deadlines/overflow!
                _pasteCoordinator.ResolveOrdinary(record);
            }
            // If Unknown, remain Pending in coordinator until 250ms deadline handles it
        }

        #endregion

        #region Private Nested Classes

        private sealed class ClipboardListenerWindow : NativeWindow, IDisposable
        {
            private readonly WindowsClipboard _parent;
            private bool _listenerAdded;
            private bool _hotkeyRegistered;
            private const int HotkeyId = 0xD011;
            private const int WM_CLIPBOARDUPDATE = 0x031D;
            private const int WM_HOTKEY = 0x0312;

            public ClipboardListenerWindow(WindowsClipboard parent)
            {
                _parent = parent;
            }

            public bool ListenerRegistered
            {
                get { return _listenerAdded; }
            }

            public bool HotkeyRegistered
            {
                get { return _hotkeyRegistered; }
            }

            public void Attach(IntPtr hwnd)
            {
                Detach();
                if (hwnd != IntPtr.Zero)
                {
                    AssignHandle(hwnd);
                    _listenerAdded = AddClipboardFormatListener(hwnd);
                    if (!_listenerAdded)
                    {
                        _parent._listenerError = "Clipboard format listener registration failed";
                    }

                    uint fsModifiers = MOD_CONTROL | MOD_SHIFT;
                    _hotkeyRegistered = RegisterHotKey(hwnd, HotkeyId, fsModifiers | MOD_NOREPEAT, VK_V);
                    if (!_hotkeyRegistered)
                    {
                        _hotkeyRegistered = RegisterHotKey(hwnd, HotkeyId, fsModifiers, VK_V);
                    }
                    if (!_hotkeyRegistered)
                    {
                        _parent._hotkeyError = "Hotkey Ctrl+Shift+V unavailable";
                    }
                }
            }

            public void Detach()
            {
                if (Handle != IntPtr.Zero)
                {
                    if (_listenerAdded)
                    {
                        RemoveClipboardFormatListener(Handle);
                        _listenerAdded = false;
                    }
                    if (_hotkeyRegistered)
                    {
                        UnregisterHotKey(Handle, HotkeyId);
                        _hotkeyRegistered = false;
                    }
                    ReleaseHandle();
                }
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_CLIPBOARDUPDATE)
                {
                    _parent.OnClipboardUpdated();
                }
                else if (m.Msg == WM_HOTKEY)
                {
                    if (m.WParam.ToInt32() == HotkeyId)
                    {
                        _parent.DispatchToggle();
                    }
                }
                base.WndProc(ref m);
            }

            public void Dispose()
            {
                Detach();
            }
        }

        private enum UiaWorkItemType
        {
            PasteResolution,
            FocusCheck
        }

        private sealed class UiaWorkItem
        {
            public readonly UiaWorkItemType Type;
            public readonly PasteRecord PasteRecord;
            public readonly uint Sequence;

            public UiaWorkItem(UiaWorkItemType type, PasteRecord pasteRecord, uint sequence)
            {
                Type = type;
                PasteRecord = pasteRecord;
                Sequence = sequence;
            }
        }

        private struct RawPasteEvent
        {
            public readonly uint Sequence;
            public readonly IntPtr ForegroundHwnd;
            public readonly IntPtr FocusedHwnd;
            public readonly int TickCount;

            public RawPasteEvent(uint seq, IntPtr fgHwnd, IntPtr focusedHwnd, int tickCount)
            {
                Sequence = seq;
                ForegroundHwnd = fgHwnd;
                FocusedHwnd = focusedHwnd;
                TickCount = tickCount;
            }
        }

        internal enum PasteState
        {
            Pending = 0,
            Ordinary = 1,
            Protected = 2
        }

        internal sealed class PasteRecord
        {
            public readonly uint Sequence;
            public readonly IntPtr ForegroundHwnd;
            public readonly IntPtr FocusedHwnd;
            public readonly int CreatedTickCount;
            public volatile bool ProtectUnknown;

            private int _state; // 0 = Pending, 1 = Ordinary, 2 = Protected
            private int _deadlineHandled;
            private int _retired;
            public int UiaProcessed;

            public PasteRecord(uint seq, IntPtr fgHwnd, IntPtr focusedHwnd, int createdTickCount, bool protectUnknown)
            {
                Sequence = seq;
                ForegroundHwnd = fgHwnd;
                FocusedHwnd = focusedHwnd;
                CreatedTickCount = createdTickCount;
                ProtectUnknown = protectUnknown;
                _state = (int)PasteState.Pending;
                _deadlineHandled = 0;
                _retired = 0;
                UiaProcessed = 0;
            }

            public PasteRecord(uint seq, IntPtr fgHwnd, int createdTickCount, bool protectUnknown)
                : this(seq, fgHwnd, fgHwnd, createdTickCount, protectUnknown)
            {
            }

            public IntPtr TargetHwnd
            {
                get { return FocusedHwnd; }
            }

            public PasteState State
            {
                get { return (PasteState)_state; }
            }

            public bool IsProtected
            {
                get { return _state == (int)PasteState.Protected; }
            }

            public bool IsOrdinary
            {
                get { return _state == (int)PasteState.Ordinary; }
            }

            public bool IsPending
            {
                get { return _state == (int)PasteState.Pending; }
            }

            public bool IsDeadlineHandled
            {
                get { return _deadlineHandled != 0; }
            }

            public bool IsRetired
            {
                get { return _retired != 0; }
            }

            public void MarkDeadlineHandled()
            {
                Interlocked.Exchange(ref _deadlineHandled, 1);
            }

            public void MarkRetired()
            {
                Interlocked.Exchange(ref _retired, 1);
            }

            public bool TryMarkOrdinary()
            {
                return Interlocked.CompareExchange(ref _state, (int)PasteState.Ordinary, (int)PasteState.Pending) == (int)PasteState.Pending;
            }

            public bool TryProtectPending()
            {
                return Interlocked.CompareExchange(ref _state, (int)PasteState.Protected, (int)PasteState.Pending) == (int)PasteState.Pending;
            }

            public bool TryMarkProtected()
            {
                while (true)
                {
                    int current = _state;
                    if (current == (int)PasteState.Protected)
                    {
                        return false;
                    }
                    if (Interlocked.CompareExchange(ref _state, (int)PasteState.Protected, current) == current)
                    {
                        return true;
                    }
                }
            }
        }

        internal sealed class PasteCoordinator
        {
            private readonly int _maxPending;
            private readonly object _lock = new object();
            private readonly List<PasteRecord> _pendingList;

            public PasteCoordinator(int maxPending = 32)
            {
                _maxPending = maxPending > 0 ? maxPending : 32;
                _pendingList = new List<PasteRecord>(_maxPending);
            }

            public int PendingCount
            {
                get
                {
                    lock (_lock)
                    {
                        return _pendingList.Count;
                    }
                }
            }

            public static uint CalculateElapsedTicks(int startTick, int currentTick)
            {
                return unchecked((uint)currentTick - (uint)startTick);
            }

            public static uint GetElapsedMs(int startTick)
            {
                return unchecked((uint)Environment.TickCount - (uint)startTick);
            }

            public PasteRecord RegisterPaste(
                uint sequence,
                IntPtr fgHwnd,
                IntPtr focusedHwnd,
                int tickCount,
                bool protectUnknown,
                out List<uint> overflowProtections)
            {
                overflowProtections = null;

                lock (_lock)
                {
                    // Coalesce identical repeated paste requests within 50ms
                    if (_pendingList.Count > 0)
                    {
                        PasteRecord newest = _pendingList[_pendingList.Count - 1];
                        if (newest.Sequence == sequence &&
                            newest.ForegroundHwnd == fgHwnd &&
                            newest.FocusedHwnd == focusedHwnd &&
                            CalculateElapsedTicks(newest.CreatedTickCount, tickCount) < 50)
                        {
                            return null;
                        }
                    }

                    // On bounded-queue overflow, ONLY unresolved evicted records with strict Unknown enabled
                    // may be conservatively protected; Ordinary records must never be censored by overflow.
                    while (_pendingList.Count >= _maxPending)
                    {
                        PasteRecord oldest = _pendingList[0];
                        _pendingList.RemoveAt(0);
                        oldest.MarkRetired();

                        if (oldest.State == PasteState.Pending && oldest.ProtectUnknown)
                        {
                            if (oldest.TryProtectPending())
                            {
                                if (overflowProtections == null)
                                {
                                    overflowProtections = new List<uint>();
                                }
                                overflowProtections.Add(oldest.Sequence);
                            }
                        }
                    }

                    PasteRecord record = new PasteRecord(sequence, fgHwnd, focusedHwnd, tickCount, protectUnknown);
                    _pendingList.Add(record);
                    return record;
                }
            }

            public bool HandleQueueEviction(PasteRecord record)
            {
                if (record == null)
                {
                    return false;
                }

                lock (_lock)
                {
                    _pendingList.Remove(record);
                }
                record.MarkRetired();

                if (record.State == PasteState.Pending && record.ProtectUnknown)
                {
                    return record.TryProtectPending();
                }
                return false;
            }

            public bool ResolveOrdinary(PasteRecord record)
            {
                if (record == null)
                {
                    return false;
                }

                bool transitioned = record.TryMarkOrdinary();
                if (transitioned)
                {
                    lock (_lock)
                    {
                        _pendingList.Remove(record);
                    }
                    record.MarkRetired();
                }
                return transitioned;
            }

            public bool ResolvePassword(PasteRecord record)
            {
                if (record == null)
                {
                    return false;
                }

                bool transitioned = record.TryMarkProtected();
                if (transitioned)
                {
                    lock (_lock)
                    {
                        _pendingList.Remove(record);
                    }
                    record.MarkRetired();
                    return true;
                }
                return false;
            }

            public List<uint> ProcessDeadlines(int currentTick)
            {
                List<uint> toProtect = null;

                lock (_lock)
                {
                    for (int i = _pendingList.Count - 1; i >= 0; i--)
                    {
                        PasteRecord record = _pendingList[i];

                        if (record.State != PasteState.Pending)
                        {
                            _pendingList.RemoveAt(i);
                            record.MarkRetired();
                            continue;
                        }

                        uint elapsed = CalculateElapsedTicks(record.CreatedTickCount, currentTick);
                        if (elapsed >= 250)
                        {
                            _pendingList.RemoveAt(i);
                            record.MarkRetired();

                            if (record.ProtectUnknown)
                            {
                                if (record.TryProtectPending())
                                {
                                    if (toProtect == null)
                                    {
                                        toProtect = new List<uint>();
                                    }
                                    toProtect.Add(record.Sequence);
                                }
                            }
                            else
                            {
                                record.MarkDeadlineHandled();
                            }
                        }
                    }
                }

                return toProtect;
            }

            public List<uint> ProcessDeadlines()
            {
                return ProcessDeadlines(Environment.TickCount);
            }

            public void Clear()
            {
                lock (_lock)
                {
                    for (int i = 0; i < _pendingList.Count; i++)
                    {
                        _pendingList[i].MarkRetired();
                    }
                    _pendingList.Clear();
                }
            }
        }

        #endregion
    }
}
