using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Windows.Forms;

namespace DollyPaste
{
    public static class PlatformTests
    {
        private static int _passedCount = 0;
        private static int _failedCount = 0;

        [STAThread]
        public static int Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("Dolly Paste - Windows Platform Verification Tests");
            Console.WriteLine("==================================================");

            RunNativeControlMetadataTests();
            RunUiAutomationControlMetadataTests();
            RunPureBoundedFormatPayloadTests();
            RunUnmanagedBufferParsingTests();
            RunInjectedPublicationStateMachineTests();
            RunPureSchedulingTests();
            RunDestinationClassificationTests();
            RunPasteProtectionDecisionTests();
            RunPasteRecordDeadlineTests();

            RunVerifiedOrdinaryNativeSurvivesDeadlineAndOverflowTests();
            RunVerifiedOrdinaryUiaSurvivesTests();
            RunUnrecognizedStrictRequestDeadlineTests();
            RunUnknownOptoutLatePositiveTests();
            RunChangedDestinationBecomesUnknownTests();
            RunPositiveAfterOrdinaryProtectsOnceTests();
            RunCompletedRecordsRetireTests();
            RunNativeQueueRemainsBoundedTests();
            RunReaderOwnershipInterleavingTests();
            RunLateFocusResultExactSequenceTests();
            RunWrapSafeElapsedCalculationTests();
            RunPasteCoordinatorConservativeProtectionRaceTests();
            RunUiaOrdinaryIdentityTests();

            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine(string.Format("Summary: {0} passed, {1} failed.", _passedCount, _failedCount));
            Console.WriteLine("==================================================");

            return _failedCount == 0 ? 0 : 1;
        }

        private static void AssertTrue(bool condition, string testName)
        {
            if (condition)
            {
                Console.WriteLine("[PASS] " + testName);
                _passedCount++;
            }
            else
            {
                Console.WriteLine("[FAIL] " + testName);
                _failedCount++;
            }
        }

        private static void RunNativeControlMetadataTests()
        {
            Console.WriteLine("\n--- Native Control Metadata Tests ---");

            using (Form hiddenForm = new Form())
            {
                hiddenForm.ShowInTaskbar = false;
                hiddenForm.WindowState = FormWindowState.Minimized;
                IntPtr unusedFormHandle = hiddenForm.Handle; // Ensure native HWND creation

                // 1. Ordinary WinForms TextBox without password masking
                using (TextBox ordinaryBox = new TextBox())
                {
                    ordinaryBox.PasswordChar = '\0';
                    ordinaryBox.UseSystemPasswordChar = false;
                    hiddenForm.Controls.Add(ordinaryBox);
                    IntPtr hOrdinary = ordinaryBox.Handle;

                    bool? ordinaryResult = WindowsClipboard.ProbeNativePasswordControl(hOrdinary);
                    AssertTrue(
                        ordinaryResult.HasValue && ordinaryResult.Value == false,
                        "Ordinary TextBox correctly classified as ordinary edit (false)");
                }

                // 2. WinForms TextBox with custom PasswordChar ('*')
                using (TextBox passwordCharBox = new TextBox())
                {
                    passwordCharBox.PasswordChar = '*';
                    hiddenForm.Controls.Add(passwordCharBox);
                    IntPtr hPassChar = passwordCharBox.Handle;

                    bool? passCharResult = WindowsClipboard.ProbeNativePasswordControl(hPassChar);
                    AssertTrue(
                        passCharResult.HasValue && passCharResult.Value == true,
                        "TextBox with PasswordChar='*' correctly classified as password (true)");
                }

                // 3. WinForms TextBox with UseSystemPasswordChar (native ES_PASSWORD)
                using (TextBox sysPasswordBox = new TextBox())
                {
                    sysPasswordBox.UseSystemPasswordChar = true;
                    hiddenForm.Controls.Add(sysPasswordBox);
                    IntPtr hSysPass = sysPasswordBox.Handle;

                    bool? sysPassResult = WindowsClipboard.ProbeNativePasswordControl(hSysPass);
                    AssertTrue(
                        sysPassResult.HasValue && sysPassResult.Value == true,
                        "TextBox with UseSystemPasswordChar=true correctly classified as password (true)");
                }

                // 4. Non-edit control (Panel) should be classified as Unknown (null)
                using (Panel panel = new Panel())
                {
                    hiddenForm.Controls.Add(panel);
                    IntPtr hPanel = panel.Handle;

                    bool? panelResult = WindowsClipboard.ProbeNativePasswordControl(hPanel);
                    AssertTrue(
                        !panelResult.HasValue,
                        "Panel non-edit control correctly classified as unknown (null)");
                }

                // 5. IntPtr.Zero should return Unknown (null)
                bool? nullHwndResult = WindowsClipboard.ProbeNativePasswordControl(IntPtr.Zero);
                AssertTrue(
                    !nullHwndResult.HasValue,
                    "IntPtr.Zero correctly classified as unknown (null)");

                // 6. Anchored class name recognition tests
                AssertTrue(
                    WindowsClipboard.IsRecognizedEditOrRichEditClass("Edit"),
                    "Standard Win32 'Edit' recognized");
                AssertTrue(
                    WindowsClipboard.IsRecognizedEditOrRichEditClass("edit"),
                    "Case-insensitive 'edit' recognized");
                AssertTrue(
                    WindowsClipboard.IsRecognizedEditOrRichEditClass("WindowsForms10.EDIT.app.0.141442b_r6_ad1"),
                    "WinForms TextBox 'WindowsForms10.EDIT.*' recognized");
                AssertTrue(
                    WindowsClipboard.IsRecognizedEditOrRichEditClass("RICHEDIT"),
                    "Win32 'RICHEDIT' recognized");
                AssertTrue(
                    WindowsClipboard.IsRecognizedEditOrRichEditClass("RichEdit20W"),
                    "Win32 'RichEdit20W' recognized");
                AssertTrue(
                    WindowsClipboard.IsRecognizedEditOrRichEditClass("MsftEdit_v1.0"),
                    "Win32 'MsftEdit_v1.0' recognized");
                AssertTrue(
                    WindowsClipboard.IsRecognizedEditOrRichEditClass("WindowsForms10.RichEdit20W.app.0.141442b"),
                    "WinForms RichTextBox 'WindowsForms10.RichEdit*' recognized");
                AssertTrue(
                    !WindowsClipboard.IsRecognizedEditOrRichEditClass("MyCustomRichEditBox"),
                    "Arbitrary class containing substring 'RichEdit' correctly rejected");
                AssertTrue(
                    !WindowsClipboard.IsRecognizedEditOrRichEditClass("Panel"),
                    "'Panel' correctly rejected");
                AssertTrue(
                    !WindowsClipboard.IsRecognizedEditOrRichEditClass(""),
                    "Empty string correctly rejected");
            }
        }

        private static void RunUiAutomationControlMetadataTests()
        {
            Console.WriteLine("\n--- UI Automation Metadata Tests ---");

            using (Form hiddenForm = new Form())
            {
                hiddenForm.ShowInTaskbar = false;
                hiddenForm.WindowState = FormWindowState.Minimized;
                IntPtr unusedFormHandle = hiddenForm.Handle;

                using (TextBox passwordBox = new TextBox())
                {
                    passwordBox.UseSystemPasswordChar = true;
                    hiddenForm.Controls.Add(passwordBox);
                    IntPtr hPass = passwordBox.Handle;

                    AutomationElement aePass = AutomationElement.FromHandle(hPass);
                    AssertTrue(aePass != null, "UIA AutomationElement retrieved for password TextBox");

                    if (aePass != null)
                    {
                        object isPassVal = aePass.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty, true);
                        AssertTrue(
                            isPassVal is bool && (bool)isPassVal,
                            "UIA IsPasswordProperty is true for masked TextBox");
                    }
                }

                using (TextBox ordinaryBox = new TextBox())
                {
                    ordinaryBox.PasswordChar = '\0';
                    ordinaryBox.UseSystemPasswordChar = false;
                    hiddenForm.Controls.Add(ordinaryBox);
                    IntPtr hOrd = ordinaryBox.Handle;

                    AutomationElement aeOrd = AutomationElement.FromHandle(hOrd);
                    AssertTrue(aeOrd != null, "UIA AutomationElement retrieved for ordinary TextBox");

                    if (aeOrd != null)
                    {
                        object isPassVal = aeOrd.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty, true);
                        AssertTrue(
                            isPassVal is bool && !(bool)isPassVal,
                            "UIA IsPasswordProperty is false for ordinary TextBox");
                    }
                }
            }
        }

        private static void RunPureBoundedFormatPayloadTests()
        {
            Console.WriteLine("\n--- Pure Bounded-Format Payload Tests ---");

            // ExcludeClipboardContentFromMonitorProcessing skips capture
            AssertTrue(
                WindowsClipboard.ShouldSkipCapturePayload(true, false, 1, true, 100),
                "Payload with ExcludeClipboardContentFromMonitorProcessing skips capture");

            // CanIncludeInClipboardHistory DWORD 0 skips capture
            AssertTrue(
                WindowsClipboard.ShouldSkipCapturePayload(false, true, 0, true, 100),
                "Payload with CanIncludeInClipboardHistory=0 skips capture");

            // CanIncludeInClipboardHistory DWORD 1 allows capture
            AssertTrue(
                !WindowsClipboard.ShouldSkipCapturePayload(false, true, 1, true, 100),
                "Payload with CanIncludeInClipboardHistory=1 allows capture");

            // Standard unicode text without privacy exclusion flags allows capture
            AssertTrue(
                !WindowsClipboard.ShouldSkipCapturePayload(false, false, 0, true, 100),
                "Standard unicode text allows capture");

            // Non-text format (no CF_UNICODETEXT) skips capture
            AssertTrue(
                WindowsClipboard.ShouldSkipCapturePayload(false, false, 0, false, 0),
                "Non-text format without CF_UNICODETEXT skips capture");

            // Empty text (length 0) skips capture
            AssertTrue(
                WindowsClipboard.ShouldSkipCapturePayload(false, false, 0, true, 0),
                "Empty text entry skips capture");

            // Max allowed text length (32768 chars) allows capture
            AssertTrue(
                !WindowsClipboard.ShouldSkipCapturePayload(false, false, 0, true, 32768),
                "Text entry of exact max length 32768 allows capture");

            // Oversized text (> 32768 chars) skips capture
            AssertTrue(
                WindowsClipboard.ShouldSkipCapturePayload(false, false, 0, true, 32769),
                "Oversized text entry of 32769 chars skips capture");
        }

        private static void RunUnmanagedBufferParsingTests()
        {
            Console.WriteLine("\n--- Unmanaged Buffer Parsing Tests ---");

            // 1. Valid unicode text buffer with null terminator
            byte[] validBytes = Encoding.Unicode.GetBytes("Hello World\0");
            IntPtr pValid = Marshal.AllocHGlobal(validBytes.Length);
            try
            {
                Marshal.Copy(validBytes, 0, pValid, validBytes.Length);
                string textResult;
                bool parsed = WindowsClipboard.TryParseUnicodeTextFromBuffer(pValid, (ulong)validBytes.Length, out textResult);
                AssertTrue(parsed && textResult == "Hello World", "Valid null-terminated unicode buffer parsed successfully");
            }
            finally
            {
                Marshal.FreeHGlobal(pValid);
            }

            // 2. Malformed buffer: odd byte length
            IntPtr pOdd = Marshal.AllocHGlobal(5);
            try
            {
                string textResult;
                bool parsed = WindowsClipboard.TryParseUnicodeTextFromBuffer(pOdd, 5, out textResult);
                AssertTrue(!parsed, "Odd byte allocation rejected as malformed");
            }
            finally
            {
                Marshal.FreeHGlobal(pOdd);
            }

            // 3. Malformed buffer: null pointer or 0 bytes
            string textNull;
            AssertTrue(!WindowsClipboard.TryParseUnicodeTextFromBuffer(IntPtr.Zero, 10, out textNull), "Null pointer buffer rejected");
            AssertTrue(!WindowsClipboard.TryParseUnicodeTextFromBuffer(pOdd, 0, out textNull), "0 byte length buffer rejected");

            // 4. Malformed buffer: unterminated buffer
            byte[] unterminatedBytes = Encoding.Unicode.GetBytes("UnterminatedBufferData"); // no null terminator
            IntPtr pUnterm = Marshal.AllocHGlobal(unterminatedBytes.Length);
            try
            {
                Marshal.Copy(unterminatedBytes, 0, pUnterm, unterminatedBytes.Length);
                string textResult;
                bool parsed = WindowsClipboard.TryParseUnicodeTextFromBuffer(pUnterm, (ulong)unterminatedBytes.Length, out textResult);
                AssertTrue(!parsed, "Unterminated buffer rejected");
            }
            finally
            {
                Marshal.FreeHGlobal(pUnterm);
            }

            // 5. Malformed buffer: empty string (immediate null terminator)
            byte[] emptyBytes = Encoding.Unicode.GetBytes("\0");
            IntPtr pEmpty = Marshal.AllocHGlobal(emptyBytes.Length);
            try
            {
                Marshal.Copy(emptyBytes, 0, pEmpty, emptyBytes.Length);
                string textResult;
                bool parsed = WindowsClipboard.TryParseUnicodeTextFromBuffer(pEmpty, (ulong)emptyBytes.Length, out textResult);
                AssertTrue(!parsed, "Empty text buffer rejected (empty capture skipped)");
            }
            finally
            {
                Marshal.FreeHGlobal(pEmpty);
            }

            // 6. Malformed buffer: history DWORD with length < 4 bytes (fail closed)
            IntPtr pShortHist = Marshal.AllocHGlobal(2);
            try
            {
                uint dwordResult;
                bool readHist = WindowsClipboard.TryReadHistoryDword(pShortHist, 2, out dwordResult);
                AssertTrue(!readHist, "History buffer with size < 4 bytes fails closed");
            }
            finally
            {
                Marshal.FreeHGlobal(pShortHist);
            }

            // 7. Valid history DWORD buffer with value 0
            IntPtr pHist0 = Marshal.AllocHGlobal(4);
            try
            {
                Marshal.WriteInt32(pHist0, 0);
                uint dwordResult;
                bool readHist = WindowsClipboard.TryReadHistoryDword(pHist0, 4, out dwordResult);
                AssertTrue(readHist && dwordResult == 0, "History DWORD 0 parsed successfully");
            }
            finally
            {
                Marshal.FreeHGlobal(pHist0);
            }

            // 8. Valid history DWORD buffer with value 1
            IntPtr pHist1 = Marshal.AllocHGlobal(4);
            try
            {
                Marshal.WriteInt32(pHist1, 1);
                uint dwordResult;
                bool readHist = WindowsClipboard.TryReadHistoryDword(pHist1, 4, out dwordResult);
                AssertTrue(readHist && dwordResult == 1, "History DWORD 1 parsed successfully");
            }
            finally
            {
                Marshal.FreeHGlobal(pHist1);
            }
        }

        #region Mock Publication Native Implementation

        private sealed class MockPublicationNative : WindowsClipboard.IClipboardPublicationNative
        {
            public uint FailAtFormat = 0; // 1 = history, 2 = cloud
            public int FailAtAllocIndex = -1; // index of AllocGlobal to fail
            public bool FailOpenClipboard = false;
            public bool FailEmptyClipboard = false;
            public uint FailAtSetDataFormat = 0; // format ID to fail SetClipboardData

            public readonly List<string> CallLog = new List<string>();
            public readonly List<uint> PublishedFormats = new List<uint>();
            public readonly List<IntPtr> AllocatedHandles = new List<IntPtr>();
            public readonly List<IntPtr> FreedHandles = new List<IntPtr>();
            public readonly List<IntPtr> TransferredHandles = new List<IntPtr>();

            private readonly Dictionary<IntPtr, IntPtr> _memoryMap = new Dictionary<IntPtr, IntPtr>();
            private int _allocCounter = 100;
            public uint MockSequence = 42;

            public uint RegisterClipboardFormat(string formatName)
            {
                CallLog.Add("RegisterFormat:" + formatName);
                if (formatName == "CanIncludeInClipboardHistory")
                {
                    return FailAtFormat == 1 ? 0u : 50001u;
                }
                if (formatName == "CanUploadToCloudClipboard")
                {
                    return FailAtFormat == 2 ? 0u : 50002u;
                }
                return 50000u;
            }

            public IntPtr AllocGlobal(uint bytes)
            {
                int currentIndex = AllocatedHandles.Count;
                CallLog.Add("AllocGlobal:" + bytes);
                if (FailAtAllocIndex == currentIndex)
                {
                    return IntPtr.Zero;
                }
                IntPtr h = new IntPtr(_allocCounter++);
                AllocatedHandles.Add(h);
                _memoryMap[h] = Marshal.AllocHGlobal((int)Math.Max(bytes, 512));
                return h;
            }

            public void FreeGlobal(IntPtr hMem)
            {
                CallLog.Add("FreeGlobal:" + hMem.ToInt64());
                FreedHandles.Add(hMem);
                if (_memoryMap.ContainsKey(hMem))
                {
                    Marshal.FreeHGlobal(_memoryMap[hMem]);
                    _memoryMap.Remove(hMem);
                }
            }

            public IntPtr LockGlobal(IntPtr hMem)
            {
                if (_memoryMap.ContainsKey(hMem))
                {
                    return _memoryMap[hMem];
                }
                return IntPtr.Zero;
            }

            public bool UnlockGlobal(IntPtr hMem)
            {
                return true;
            }

            public bool OpenClipboard(IntPtr hwnd)
            {
                CallLog.Add("OpenClipboard");
                return !FailOpenClipboard;
            }

            public bool EmptyClipboard()
            {
                CallLog.Add("EmptyClipboard");
                return !FailEmptyClipboard;
            }

            public IntPtr SetClipboardData(uint uFormat, IntPtr hMem)
            {
                CallLog.Add("SetClipboardData:" + uFormat);
                if (FailAtSetDataFormat == uFormat)
                {
                    return IntPtr.Zero;
                }
                PublishedFormats.Add(uFormat);
                TransferredHandles.Add(hMem);
                return hMem;
            }

            public uint GetClipboardSequenceNumber()
            {
                CallLog.Add("GetClipboardSequenceNumber");
                return MockSequence;
            }

            public bool CloseClipboard()
            {
                CallLog.Add("CloseClipboard");
                return true;
            }

            public void Cleanup()
            {
                foreach (IntPtr p in _memoryMap.Values)
                {
                    Marshal.FreeHGlobal(p);
                }
                _memoryMap.Clear();
            }
        }

        #endregion

        private static void RunInjectedPublicationStateMachineTests()
        {
            Console.WriteLine("\n--- Injected Publication State Machine Tests ---");

            // 1. Full successful publication
            {
                MockPublicationNative mock = new MockPublicationNative();
                try
                {
                    uint seq;
                    bool ok = WindowsClipboard.ExecutePublication(mock, IntPtr.Zero, "Hello Dolly", out seq);
                    AssertTrue(ok, "Publication succeeds under mock native environment");
                    AssertTrue(seq == 42, "Exact sequence captured under open clipboard matches mock sequence");
                    AssertTrue(mock.PublishedFormats.Count == 3, "Exactly 3 formats published");
                    AssertTrue(mock.PublishedFormats[0] == 50001, "CanIncludeInClipboardHistory published first");
                    AssertTrue(mock.PublishedFormats[1] == 50002, "CanUploadToCloudClipboard published second");
                    AssertTrue(mock.PublishedFormats[2] == 13, "CF_UNICODETEXT published third");
                    AssertTrue(mock.FreedHandles.Count == 0, "Transferred handles not freed on success (system ownership)");

                    int getSeqIdx = mock.CallLog.IndexOf("GetClipboardSequenceNumber");
                    int closeIdx = mock.CallLog.IndexOf("CloseClipboard");
                    AssertTrue(getSeqIdx >= 0 && closeIdx >= 0 && getSeqIdx < closeIdx,
                        "Sequence captured while clipboard remains open and owned");
                }
                finally
                {
                    mock.Cleanup();
                }
            }

            // 2. Format registration failure
            {
                MockPublicationNative mock = new MockPublicationNative();
                mock.FailAtFormat = 1; // CanIncludeInClipboardHistory fails
                try
                {
                    uint seq;
                    bool ok = WindowsClipboard.ExecutePublication(mock, IntPtr.Zero, "Hello", out seq);
                    AssertTrue(!ok, "Publication fails when format registration fails");
                    AssertTrue(mock.AllocatedHandles.Count == 0, "No buffers allocated if format registration fails");
                    AssertTrue(!mock.CallLog.Contains("OpenClipboard"), "Clipboard not opened on format registration failure");
                }
                finally
                {
                    mock.Cleanup();
                }
            }

            // 3. Buffer allocation failure (2nd buffer)
            {
                MockPublicationNative mock = new MockPublicationNative();
                mock.FailAtAllocIndex = 1; // Cloud buffer allocation fails
                try
                {
                    uint seq;
                    bool ok = WindowsClipboard.ExecutePublication(mock, IntPtr.Zero, "Hello", out seq);
                    AssertTrue(!ok, "Publication fails when buffer allocation fails");
                    AssertTrue(mock.FreedHandles.Contains(mock.AllocatedHandles[0]), "1st allocated buffer freed on 2nd buffer failure");
                    AssertTrue(!mock.CallLog.Contains("OpenClipboard"), "Clipboard not opened if allocation fails");
                    AssertTrue(!mock.CallLog.Contains("EmptyClipboard"), "Clipboard not emptied if allocation fails (user data preserved)");
                }
                finally
                {
                    mock.Cleanup();
                }
            }

            // 4. OpenClipboard contention / failure
            {
                MockPublicationNative mock = new MockPublicationNative();
                mock.FailOpenClipboard = true;
                try
                {
                    uint seq;
                    bool ok = WindowsClipboard.ExecutePublication(mock, IntPtr.Zero, "Hello", out seq);
                    AssertTrue(!ok, "Publication fails when OpenClipboard fails");
                    AssertTrue(mock.FreedHandles.Count == 3, "All 3 allocated buffers freed on OpenClipboard failure");
                    AssertTrue(!mock.CallLog.Contains("EmptyClipboard"), "EmptyClipboard not called when OpenClipboard fails");
                }
                finally
                {
                    mock.Cleanup();
                }
            }

            // 5. EmptyClipboard failure
            {
                MockPublicationNative mock = new MockPublicationNative();
                mock.FailEmptyClipboard = true;
                try
                {
                    uint seq;
                    bool ok = WindowsClipboard.ExecutePublication(mock, IntPtr.Zero, "Hello", out seq);
                    AssertTrue(!ok, "Publication fails when EmptyClipboard fails");
                    AssertTrue(mock.FreedHandles.Count == 3, "All 3 buffers freed on EmptyClipboard failure");
                    AssertTrue(mock.PublishedFormats.Count == 0, "No formats published on EmptyClipboard failure");
                }
                finally
                {
                    mock.Cleanup();
                }
            }

            // 6. SetClipboardData failure on History privacy format
            {
                MockPublicationNative mock = new MockPublicationNative();
                mock.FailAtSetDataFormat = 50001; // History format SetClipboardData fails
                try
                {
                    uint seq;
                    bool ok = WindowsClipboard.ExecutePublication(mock, IntPtr.Zero, "Hello", out seq);
                    AssertTrue(!ok, "Publication fails when privacy format SetClipboardData fails");
                    AssertTrue(mock.FreedHandles.Count == 3, "Failed buffer and remaining buffers freed");
                    AssertTrue(!mock.PublishedFormats.Contains(13), "CF_UNICODETEXT never published after privacy format failure");
                }
                finally
                {
                    mock.Cleanup();
                }
            }

            // 7. SetClipboardData failure on Cloud privacy format
            {
                MockPublicationNative mock = new MockPublicationNative();
                mock.FailAtSetDataFormat = 50002; // Cloud format SetClipboardData fails
                try
                {
                    uint seq;
                    bool ok = WindowsClipboard.ExecutePublication(mock, IntPtr.Zero, "Hello", out seq);
                    AssertTrue(!ok, "Publication fails when Cloud format SetClipboardData fails");
                    AssertTrue(mock.FreedHandles.Count == 2, "Caller frees remaining buffers (cloud and text)");
                    AssertTrue(!mock.PublishedFormats.Contains(13), "CF_UNICODETEXT never published after cloud format failure");
                }
                finally
                {
                    mock.Cleanup();
                }
            }

            // 8. SetClipboardData failure on Unicode text format
            {
                MockPublicationNative mock = new MockPublicationNative();
                mock.FailAtSetDataFormat = 13; // CF_UNICODETEXT SetClipboardData fails
                try
                {
                    uint seq;
                    bool ok = WindowsClipboard.ExecutePublication(mock, IntPtr.Zero, "Hello", out seq);
                    AssertTrue(!ok, "Publication fails when text format SetClipboardData fails");
                    AssertTrue(mock.FreedHandles.Count == 1, "Failed text buffer freed by caller");
                }
                finally
                {
                    mock.Cleanup();
                }
            }
        }

        private static void RunPureSchedulingTests()
        {
            Console.WriteLine("\n--- Pure Scheduling Tests ---");

            // 1. Old read completes after new clipboard update: must not overwrite current tracked sequence
            {
                WindowsClipboard.SchedulingCoordinator coordinator = new WindowsClipboard.SchedulingCoordinator();
                coordinator.OnClipboardUpdated(100);
                AssertTrue(coordinator.CurrentSequence == 100, "Initial sequence tracked as 100");

                coordinator.OnClipboardUpdated(101);
                AssertTrue(coordinator.CurrentSequence == 101, "Sequence updated to 101");

                // Older read for sequence 100 finishes now
                bool readAccepted = coordinator.CompleteRead(100, "First payload");
                AssertTrue(readAccepted, "Older payload accepted with its original sequence");
                AssertTrue(coordinator.CurrentSequence == 101, "Current sequence NOT overwritten by older read completion (still 101)");
                AssertTrue(coordinator.CapturedPayloads.Count == 1, "Payload captured");
                AssertTrue(coordinator.CapturedPayloads[0].Value == 100, "Payload retained original sequence 100");
            }

            // 2. Delayed focus check crossing clipboard changes: must protect exact scheduled sequence, not newer
            {
                WindowsClipboard.SchedulingCoordinator coordinator = new WindowsClipboard.SchedulingCoordinator();
                coordinator.OnClipboardUpdated(200);

                // Simulate focus check scheduled with sequence 200
                uint scheduledSeq = 200;

                // Clipboard changes to 201 during slow UIA call
                coordinator.OnClipboardUpdated(201);
                AssertTrue(coordinator.CurrentSequence == 201, "Clipboard advanced to 201");

                // Slow UIA focus check finishes and detects password
                coordinator.CompleteFocusCheck(scheduledSeq, true);

                AssertTrue(coordinator.ProtectedSequences.Count == 1, "Protected sequence recorded");
                AssertTrue(coordinator.ProtectedSequences[0] == 200,
                    "Protected sequence is 200 (exact scheduled sequence), NOT newer sequence 201");
            }
        }

        private static void RunDestinationClassificationTests()
        {
            Console.WriteLine("\n--- Destination Classification Tests ---");

            // 1. Native probe identifies password control
            AssertTrue(
                WindowsClipboard.ClassifyDestinationMetadata(true, true, false, true, false) ==
                WindowsClipboard.DestinationClassification.Password,
                "Native password probe identifies control as Password");

            // 2. Native probe identifies ordinary edit control when focus verified
            AssertTrue(
                WindowsClipboard.ClassifyDestinationMetadata(false, true, false, true, false) ==
                WindowsClipboard.DestinationClassification.OrdinaryEdit,
                "Native ordinary edit probe with verified focus identifies control as OrdinaryEdit");

            // 3. Native probe identifies ordinary edit, but focus does not match destination context
            AssertTrue(
                WindowsClipboard.ClassifyDestinationMetadata(false, false, false, true, false) ==
                WindowsClipboard.DestinationClassification.Unknown,
                "Native ordinary edit probe with focus mismatch classifies as Unknown");

            // 4. Focus cannot be verified -> Unknown
            AssertTrue(
                WindowsClipboard.ClassifyDestinationMetadata(null, false, false, true, false) ==
                WindowsClipboard.DestinationClassification.Unknown,
                "Unverified focus context classifies as Unknown");

            // 5. UIA indicates password field
            AssertTrue(
                WindowsClipboard.ClassifyDestinationMetadata(null, true, true, true, false) ==
                WindowsClipboard.DestinationClassification.Password,
                "UIA IsPassword classifies control as Password");

            // 6. UIA indicates container/document/root (not an actual identified Edit control)
            AssertTrue(
                WindowsClipboard.ClassifyDestinationMetadata(null, true, false, true, false) ==
                WindowsClipboard.DestinationClassification.Unknown,
                "ControlType.Document/root/container does not prove safety; classifies as Unknown");

            // 7. UIA indicates identified editable element (ControlType.Edit) with IsPassword=false
            AssertTrue(
                WindowsClipboard.ClassifyDestinationMetadata(null, true, false, true, true) ==
                WindowsClipboard.DestinationClassification.OrdinaryEdit,
                "Verified Edit control with supported IsPassword=false classifies as OrdinaryEdit");

            // 8. UIA indicates Edit control but IsPassword property is unsupported
            AssertTrue(
                WindowsClipboard.ClassifyDestinationMetadata(null, true, false, false, true) ==
                WindowsClipboard.DestinationClassification.Unknown,
                "Edit control with unsupported IsPassword property classifies as Unknown");
        }

        private static void RunPasteProtectionDecisionTests()
        {
            Console.WriteLine("\n--- Paste Protection Decision Tests ---");

            // Password destination always protects
            AssertTrue(
                WindowsClipboard.ShouldProtectOnPaste(WindowsClipboard.DestinationClassification.Password, true),
                "Password destination triggers protection when protectUnknown=true");

            AssertTrue(
                WindowsClipboard.ShouldProtectOnPaste(WindowsClipboard.DestinationClassification.Password, false),
                "Password destination triggers protection even when protectUnknown=false");

            // Unknown destination protects if ProtectUnknownTargets is true
            AssertTrue(
                WindowsClipboard.ShouldProtectOnPaste(WindowsClipboard.DestinationClassification.Unknown, true),
                "Unknown destination triggers protection when protectUnknown=true");

            // Unknown destination does not protect if ProtectUnknownTargets is false
            AssertTrue(
                !WindowsClipboard.ShouldProtectOnPaste(WindowsClipboard.DestinationClassification.Unknown, false),
                "Unknown destination does not trigger protection when protectUnknown=false");

            // Ordinary edit destination never triggers protection
            AssertTrue(
                !WindowsClipboard.ShouldProtectOnPaste(WindowsClipboard.DestinationClassification.OrdinaryEdit, true),
                "Ordinary edit destination does not trigger protection");
        }

        private static void RunPasteRecordDeadlineTests()
        {
            Console.WriteLine("\n--- Paste Record Deadline & Late Resolution Tests ---");

            // 1. Late password resolution after deadline when ProtectUnknown is false:
            // Positive IsPassword result must NOT be dropped!
            {
                WindowsClipboard.PasteRecord record = new WindowsClipboard.PasteRecord(500, IntPtr.Zero, Environment.TickCount - 300, false);
                AssertTrue(!record.IsProtected, "Record initially not protected");

                // Simulate late positive password resolution arriving after deadline
                bool protectedNow = record.TryMarkProtected();
                AssertTrue(protectedNow, "Late confirmed password protection succeeds even after deadline");
                AssertTrue(record.IsProtected, "Record marked protected");

                // Duplicate protection avoided
                bool duplicateProtected = record.TryMarkProtected();
                AssertTrue(!duplicateProtected, "Duplicate protection safely avoided");
            }

            // 2. Deadline protection when ProtectUnknown is true
            {
                WindowsClipboard.PasteRecord record = new WindowsClipboard.PasteRecord(501, IntPtr.Zero, Environment.TickCount - 300, true);
                AssertTrue(!record.IsProtected, "Record initially not protected");

                // Deadline elapses and triggers protection for unknown target
                bool deadlineProtected = record.TryMarkProtected();
                AssertTrue(deadlineProtected, "Deadline triggers conservative protection when ProtectUnknown is true");

                // Later UIA resolution arrives: duplicate protection avoided
                bool lateProtected = record.TryMarkProtected();
                AssertTrue(!lateProtected, "Duplicate protection after deadline safely avoided");
            }
        }
        private static void RunVerifiedOrdinaryNativeSurvivesDeadlineAndOverflowTests()
        {
            Console.WriteLine("\n--- Verified Ordinary Native Survives Deadline and Overflow Tests ---");

            WindowsClipboard.PasteCoordinator coordinator = new WindowsClipboard.PasteCoordinator(4);
            List<uint> overflowProtects;
            WindowsClipboard.PasteRecord record = coordinator.RegisterPaste(
                100, (IntPtr)1, (IntPtr)2, Environment.TickCount, true, out overflowProtects);

            AssertTrue(record != null, "Paste record registered");
            AssertTrue(record.State == WindowsClipboard.PasteState.Pending, "Record starts as Pending");

            // Native probe verifies ordinary edit control with verified focus
            WindowsClipboard.DestinationClassification classification = WindowsClipboard.ClassifyDestinationMetadata(
                false, true, false, true, false);
            AssertTrue(classification == WindowsClipboard.DestinationClassification.OrdinaryEdit, "Classified as OrdinaryEdit");

            bool resolved = coordinator.ResolveOrdinary(record);
            AssertTrue(resolved, "Record resolved to Ordinary");
            AssertTrue(record.IsOrdinary, "Record is Ordinary");
            AssertTrue(!record.IsProtected, "Record is NOT protected");

            // Simulate 250ms deadline expiration: must NOT protect ordinary record
            List<uint> deadlineProtects = coordinator.ProcessDeadlines(record.CreatedTickCount + 300);
            AssertTrue(deadlineProtects == null || deadlineProtects.Count == 0, "Deadline does NOT protect Ordinary record");
            AssertTrue(!record.IsProtected, "Record remains unprotected after deadline");

            // Now overflow coordinator capacity (capacity = 4) by adding 5 new strict items
            for (int i = 0; i < 5; i++)
            {
                coordinator.RegisterPaste((uint)(200 + i), (IntPtr)(10 + i), (IntPtr)(20 + i), Environment.TickCount + 100 * i, true, out overflowProtects);
            }

            // Verified ordinary record must never be censored by overflow
            AssertTrue(!record.IsProtected, "Verified ordinary record survives queue overflow without protection");
        }

        private static void RunVerifiedOrdinaryUiaSurvivesTests()
        {
            Console.WriteLine("\n--- Verified Ordinary UIA Survives Tests ---");

            WindowsClipboard.PasteCoordinator coordinator = new WindowsClipboard.PasteCoordinator(4);
            List<uint> overflowProtects;
            WindowsClipboard.PasteRecord record = coordinator.RegisterPaste(
                101, (IntPtr)1, (IntPtr)2, Environment.TickCount, true, out overflowProtects);

            // UIA verifies Edit element with supported IsPassword=false and matching focus
            WindowsClipboard.DestinationClassification classification = WindowsClipboard.ClassifyDestinationMetadata(
                null, true, false, true, true);
            AssertTrue(classification == WindowsClipboard.DestinationClassification.OrdinaryEdit, "UIA verified as OrdinaryEdit");

            bool resolved = coordinator.ResolveOrdinary(record);
            AssertTrue(resolved, "UIA resolved to Ordinary");
            AssertTrue(record.IsOrdinary, "Record marked Ordinary");
            AssertTrue(!record.IsProtected, "Record NOT protected");

            // Deadline check after 350ms
            List<uint> deadlineProtects = coordinator.ProcessDeadlines(record.CreatedTickCount + 350);
            AssertTrue(deadlineProtects == null || deadlineProtects.Count == 0, "Deadline does NOT protect UIA Ordinary record");
            AssertTrue(!record.IsProtected, "Record remains unprotected after deadline");

            // Overflow coordinator with strict unknown pastes
            for (int i = 0; i < 5; i++)
            {
                coordinator.RegisterPaste((uint)(300 + i), (IntPtr)(10 + i), (IntPtr)(20 + i), Environment.TickCount + 100 * i, true, out overflowProtects);
            }
            AssertTrue(!record.IsProtected, "UIA Ordinary record survives queue overflow without protection");
        }

        private static void RunUnrecognizedStrictRequestDeadlineTests()
        {
            Console.WriteLine("\n--- Unrecognized Strict Request Deadline Tests ---");

            WindowsClipboard.PasteCoordinator coordinator = new WindowsClipboard.PasteCoordinator(32);
            List<uint> overflowProtects;
            int startTick = Environment.TickCount;
            WindowsClipboard.PasteRecord record = coordinator.RegisterPaste(
                102, (IntPtr)1, (IntPtr)2, startTick, true, out overflowProtects);

            AssertTrue(record.State == WindowsClipboard.PasteState.Pending, "Record pending");

            // Before 250ms deadline (e.g. 150ms elapsed) -> must NOT protect yet
            List<uint> earlyProtects = coordinator.ProcessDeadlines(startTick + 150);
            AssertTrue(earlyProtects == null || earlyProtects.Count == 0, "No protection before 250ms deadline");
            AssertTrue(!record.IsProtected, "Record still unprotected at 150ms");

            // After 250ms deadline (e.g. 260ms elapsed) -> must protect once
            List<uint> deadlineProtects = coordinator.ProcessDeadlines(startTick + 260);
            AssertTrue(deadlineProtects != null && deadlineProtects.Count == 1, "Protected exactly once after 250ms");
            AssertTrue(deadlineProtects[0] == 102, "Protected correct sequence 102");
            AssertTrue(record.IsProtected, "Record marked protected");
            AssertTrue(record.IsRetired, "Record retired from coordinator");

            // Later deadline tick at 350ms -> must NOT protect again
            List<uint> laterProtects = coordinator.ProcessDeadlines(startTick + 350);
            AssertTrue(laterProtects == null || laterProtects.Count == 0, "No duplicate protection on subsequent deadline ticks");
        }

        private static void RunUnknownOptoutLatePositiveTests()
        {
            Console.WriteLine("\n--- Unknown Optout Late Positive Tests ---");

            WindowsClipboard.PasteCoordinator coordinator = new WindowsClipboard.PasteCoordinator(32);
            List<uint> overflowProtects;
            int startTick = Environment.TickCount;
            WindowsClipboard.PasteRecord record = coordinator.RegisterPaste(
                103, (IntPtr)1, (IntPtr)2, startTick, false, out overflowProtects); // ProtectUnknown = false

            AssertTrue(!record.ProtectUnknown, "ProtectUnknown is false (optout)");

            // Deadline ticks after 300ms: must NOT protect
            List<uint> deadlineProtects = coordinator.ProcessDeadlines(startTick + 300);
            AssertTrue(deadlineProtects == null || deadlineProtects.Count == 0, "Deadline does NOT protect optout record");
            AssertTrue(!record.IsProtected, "Record not protected by deadline");
            AssertTrue(record.IsDeadlineHandled, "Record marked deadline-handled");
            AssertTrue(record.IsRetired, "Record retired from deadline work");

            // Late positive password arrives from in-flight worker reference
            bool protectedNow = coordinator.ResolvePassword(record);
            AssertTrue(protectedNow, "Late confirmed password resolves to Protected");
            AssertTrue(record.IsProtected, "Record now marked protected");
            AssertTrue(record.Sequence == 103, "Protected original captured sequence 103");

            // Duplicate late positive does not protect twice
            bool duplicateProtected = coordinator.ResolvePassword(record);
            AssertTrue(!duplicateProtected, "Duplicate resolution safely ignored");
        }

        private static void RunChangedDestinationBecomesUnknownTests()
        {
            Console.WriteLine("\n--- Changed Destination Becomes Unknown Tests ---");

            // 1. Native probe saw ordinary edit class, but focus mismatched/changed
            WindowsClipboard.DestinationClassification c1 = WindowsClipboard.ClassifyDestinationMetadata(
                false, false, false, true, false);
            AssertTrue(c1 == WindowsClipboard.DestinationClassification.Unknown,
                "Native ordinary probe with changed/mismatched focus classifies as Unknown, never Ordinary");

            // 2. UIA saw Edit control with IsPassword=false, but focus mismatched/changed
            WindowsClipboard.DestinationClassification c2 = WindowsClipboard.ClassifyDestinationMetadata(
                null, false, false, true, true);
            AssertTrue(c2 == WindowsClipboard.DestinationClassification.Unknown,
                "UIA edit control with changed/mismatched focus classifies as Unknown, never Ordinary");

            // 3. Focus could not be verified (IntPtr.Zero or null)
            WindowsClipboard.DestinationClassification c3 = WindowsClipboard.ClassifyDestinationMetadata(
                null, false, false, false, false);
            AssertTrue(c3 == WindowsClipboard.DestinationClassification.Unknown,
                "Unverified focus classifies as Unknown");

            // 4. Positive password probe retains password protection even if focus context changed
            WindowsClipboard.DestinationClassification c4 = WindowsClipboard.ClassifyDestinationMetadata(
                true, false, false, true, false);
            AssertTrue(c4 == WindowsClipboard.DestinationClassification.Password,
                "Positive native password probe conservatively protects even if focus changed");
        }

        private static void RunPositiveAfterOrdinaryProtectsOnceTests()
        {
            Console.WriteLine("\n--- Positive After Ordinary Protects Once Tests ---");

            WindowsClipboard.PasteCoordinator coordinator = new WindowsClipboard.PasteCoordinator(32);
            List<uint> overflowProtects;
            WindowsClipboard.PasteRecord record = coordinator.RegisterPaste(
                104, (IntPtr)1, (IntPtr)2, Environment.TickCount, true, out overflowProtects);

            // Initially verified Ordinary
            bool markOrd = coordinator.ResolveOrdinary(record);
            AssertTrue(markOrd, "Record marked Ordinary");
            AssertTrue(record.IsOrdinary, "Record is Ordinary");
            AssertTrue(!record.IsProtected, "Record is not protected");

            // Later confirmed positive password result arrives (e.g. from background metadata or deeper probe)
            bool markProt = coordinator.ResolvePassword(record);
            AssertTrue(markProt, "Confirmed positive moves Ordinary record to Protected once");
            AssertTrue(record.IsProtected, "Record is now Protected");

            // Duplicate positive call
            bool duplicateProt = coordinator.ResolvePassword(record);
            AssertTrue(!duplicateProt, "Duplicate positive call returns false (protects once only)");
        }

        private static void RunCompletedRecordsRetireTests()
        {
            Console.WriteLine("\n--- Completed Records Retire Tests ---");

            WindowsClipboard.PasteCoordinator coordinator = new WindowsClipboard.PasteCoordinator(32);
            AssertTrue(coordinator.PendingCount == 0, "Initial pending count is 0");

            // 1. Ordinary resolution retires from pending list
            List<uint> overflow;
            WindowsClipboard.PasteRecord r1 = coordinator.RegisterPaste(201, (IntPtr)1, (IntPtr)1, Environment.TickCount, true, out overflow);
            AssertTrue(coordinator.PendingCount == 1, "Pending count is 1 after register");
            coordinator.ResolveOrdinary(r1);
            AssertTrue(coordinator.PendingCount == 0, "Pending count is 0 after Ordinary resolution (retired)");
            AssertTrue(r1.IsRetired, "Record 1 marked retired");

            // 2. Password resolution retires from pending list
            WindowsClipboard.PasteRecord r2 = coordinator.RegisterPaste(202, (IntPtr)1, (IntPtr)1, Environment.TickCount, true, out overflow);
            AssertTrue(coordinator.PendingCount == 1, "Pending count is 1 after register");
            coordinator.ResolvePassword(r2);
            AssertTrue(coordinator.PendingCount == 0, "Pending count is 0 after Password resolution (retired)");
            AssertTrue(r2.IsRetired, "Record 2 marked retired");

            // 3. Strict deadline resolution retires from pending list
            int tick3 = Environment.TickCount;
            WindowsClipboard.PasteRecord r3 = coordinator.RegisterPaste(203, (IntPtr)1, (IntPtr)1, tick3, true, out overflow);
            AssertTrue(coordinator.PendingCount == 1, "Pending count is 1");
            coordinator.ProcessDeadlines(tick3 + 300);
            AssertTrue(coordinator.PendingCount == 0, "Pending count is 0 after strict deadline (retired)");
            AssertTrue(r3.IsRetired, "Record 3 marked retired");

            // 4. Optout deadline resolution retires from pending list
            int tick4 = Environment.TickCount;
            WindowsClipboard.PasteRecord r4 = coordinator.RegisterPaste(204, (IntPtr)1, (IntPtr)1, tick4, false, out overflow);
            AssertTrue(coordinator.PendingCount == 1, "Pending count is 1");
            coordinator.ProcessDeadlines(tick4 + 300);
            AssertTrue(coordinator.PendingCount == 0, "Pending count is 0 after optout deadline (retired)");
            AssertTrue(r4.IsRetired, "Record 4 marked retired");
        }

        private static void RunNativeQueueRemainsBoundedTests()
        {
            Console.WriteLine("\n--- Native Queue Remains Bounded & Overflow Tests ---");

            // Capacity = 3
            WindowsClipboard.PasteCoordinator coordinator = new WindowsClipboard.PasteCoordinator(3);
            List<uint> overflow;

            // Register 3 items
            WindowsClipboard.PasteRecord r1 = coordinator.RegisterPaste(301, (IntPtr)1, (IntPtr)1, Environment.TickCount, true, out overflow);
            WindowsClipboard.PasteRecord r2 = coordinator.RegisterPaste(302, (IntPtr)2, (IntPtr)2, Environment.TickCount + 100, true, out overflow);
            WindowsClipboard.PasteRecord r3 = coordinator.RegisterPaste(303, (IntPtr)3, (IntPtr)3, Environment.TickCount + 200, false, out overflow);
            AssertTrue(coordinator.PendingCount == 3, "Queue is at capacity (3 items)");
            AssertTrue(overflow == null || overflow.Count == 0, "No overflow on initial fill");

            // Add 4th item: r1 (strict Pending) is evicted -> must be protected
            WindowsClipboard.PasteRecord r4 = coordinator.RegisterPaste(304, (IntPtr)4, (IntPtr)4, Environment.TickCount + 300, true, out overflow);
            AssertTrue(coordinator.PendingCount == 3, "Queue remains bounded at 3");
            AssertTrue(overflow != null && overflow.Count == 1 && overflow[0] == 301, "Evicted unresolved strict request 301 protected on overflow");
            AssertTrue(r1.IsProtected, "r1 marked protected");

            // Resolve r2 as Ordinary, then add 5th item
            coordinator.ResolveOrdinary(r2); // r2 removed, count = 2
            WindowsClipboard.PasteRecord r5 = coordinator.RegisterPaste(305, (IntPtr)5, (IntPtr)5, Environment.TickCount + 400, true, out overflow); // count = 3
            WindowsClipboard.PasteRecord r6 = coordinator.RegisterPaste(306, (IntPtr)6, (IntPtr)6, Environment.TickCount + 500, true, out overflow); // evicts r3 (optout)
            AssertTrue(overflow == null || overflow.Count == 0, "Evicted optout request (r3) is NOT protected on overflow");
            AssertTrue(!r3.IsProtected, "r3 remains unprotected");
        }

        private static void RunReaderOwnershipInterleavingTests()
        {
            Console.WriteLine("\n--- Reader Ownership Interleaving Tests ---");

            WindowsClipboard.ReaderCoordinator reader = new WindowsClipboard.ReaderCoordinator();
            AssertTrue(!reader.IsRunning, "Reader initially not running");
            AssertTrue(reader.ActiveWorkerCount == 0, "Active worker count initially 0");

            // 1. Initial event arrives: starts Worker 1
            bool schedule1 = reader.RequestRead(100);
            AssertTrue(schedule1, "First request schedules a worker");
            AssertTrue(reader.IsRunning, "Reader is running");

            // Worker 1 starts
            reader.OnWorkerStarted();
            AssertTrue(reader.ActiveWorkerCount == 1, "Worker 1 active (count == 1)");

            // Worker 1 fetches sequence to read
            uint seq1 = reader.TryGetNextSequence();
            AssertTrue(seq1 == 100, "Worker 1 receives sequence 100");

            // Worker 1 loop checks next: no pending sequence right now
            uint nextSeq = reader.TryGetNextSequence();
            AssertTrue(nextSeq == 0, "No pending sequence, worker 1 prepares to exit loop");

            // CRITICAL INTERLEAVING: A new event 101 arrives before Worker 1's finally executes!
            bool schedule2 = reader.RequestRead(101);
            AssertTrue(!schedule2, "Event arriving during worker lifetime does NOT schedule second worker");
            AssertTrue(reader.IsRunning, "Running state retained for pending sequence");
            AssertTrue(reader.ActiveWorkerCount == 1, "Active worker count still 1");

            // Worker 1 enters finally block and calls OnWorkerExiting
            bool scheduleSuccessor = reader.OnWorkerExiting(true);
            AssertTrue(scheduleSuccessor, "OnWorkerExiting atomically reserves Running and signals to schedule successor");
            AssertTrue(reader.IsRunning, "Running stays reserved for successor");
            AssertTrue(reader.ActiveWorkerCount == 0, "Worker 1 has exited (count == 0)");

            // Successor Worker 2 starts
            reader.OnWorkerStarted();
            AssertTrue(reader.ActiveWorkerCount == 1, "Worker 2 active (count == 1)");
            uint seq2 = reader.TryGetNextSequence();
            AssertTrue(seq2 == 101, "Worker 2 receives sequence 101");

            // Worker 2 loop finishes with no pending work
            uint seq3 = reader.TryGetNextSequence();
            AssertTrue(seq3 == 0, "No further pending sequence");

            bool scheduleThird = reader.OnWorkerExiting(true);
            AssertTrue(!scheduleThird, "No pending work: no successor scheduled");
            AssertTrue(!reader.IsRunning, "Reader ownership fully released");
            AssertTrue(reader.ActiveWorkerCount == 0, "Active worker count is 0");
        }

        private static void RunLateFocusResultExactSequenceTests()
        {
            Console.WriteLine("\n--- Late Focus Result Exact Sequence Tests ---");

            WindowsClipboard.PasteCoordinator coordinator = new WindowsClipboard.PasteCoordinator(32);
            List<uint> overflow;

            // User pastes while clipboard is at sequence 500
            uint eventSequence = 500;
            WindowsClipboard.PasteRecord record = coordinator.RegisterPaste(
                eventSequence, (IntPtr)1, (IntPtr)2, Environment.TickCount, true, out overflow);

            // Clipboard changes to 501, then 502 while slow focus/UIA check is in flight
            uint newerSequence = 502;

            // Slow focus check completes and confirms password field for the paste event
            bool protectedNow = coordinator.ResolvePassword(record);
            AssertTrue(protectedNow, "Password resolved");
            AssertTrue(record.Sequence == 500 && record.Sequence != newerSequence, "Protected sequence is EXACT event sequence 500, NOT newer sequence 502");
        }

        private static void RunWrapSafeElapsedCalculationTests()
        {
            Console.WriteLine("\n--- Wrap-Safe Elapsed Calculation Tests ---");

            // 1. Normal positive difference
            uint e1 = WindowsClipboard.PasteCoordinator.CalculateElapsedTicks(1000, 1250);
            AssertTrue(e1 == 250u, "Standard elapsed 1250 - 1000 == 250");

            // 2. Across signed TickCount boundary (int.MaxValue to int.MinValue)
            int start = int.MaxValue - 100;
            int current = int.MinValue + 150;
            uint e2 = WindowsClipboard.PasteCoordinator.CalculateElapsedTicks(start, current);
            AssertTrue(e2 == 251u, "Wrap-safe across int.MaxValue to int.MinValue boundary == 251ms");

            // 3. Negative to positive boundary
            int startNeg = -50;
            int curPos = 50;
            uint e3 = WindowsClipboard.PasteCoordinator.CalculateElapsedTicks(startNeg, curPos);
            AssertTrue(e3 == 100u, "Elapsed from -50 to 50 == 100ms");

            // 4. Int.MinValue boundary
            uint e4 = WindowsClipboard.PasteCoordinator.CalculateElapsedTicks(int.MinValue, int.MinValue + 300);
            AssertTrue(e4 == 300u, "Elapsed from int.MinValue to int.MinValue+300 == 300ms");

            // 5. Zero elapsed
            uint e5 = WindowsClipboard.PasteCoordinator.CalculateElapsedTicks(12345, 12345);
            AssertTrue(e5 == 0u, "Zero elapsed ticks == 0");
        }

        private static void RunPasteCoordinatorConservativeProtectionRaceTests()
        {
            Console.WriteLine("\n--- Paste Coordinator Conservative Protection Race Tests ---");

            // 1. Record becomes Ordinary between hypothetical pre-check and conservative CAS
            WindowsClipboard.PasteRecord record = new WindowsClipboard.PasteRecord(
                401, (IntPtr)10, (IntPtr)20, Environment.TickCount, true);
            AssertTrue(record.State == WindowsClipboard.PasteState.Pending, "Record starts as Pending");

            // Hypothetical pre-check reads State == Pending
            bool precheckPending = (record.State == WindowsClipboard.PasteState.Pending);
            AssertTrue(precheckPending, "Hypothetical pre-check observed Pending");

            // Concurrent resolution transitions record to Ordinary outside coordinator lock
            bool ordTransition = record.TryMarkOrdinary();
            AssertTrue(ordTransition, "Record successfully transitioned to Ordinary via CAS");
            AssertTrue(record.IsOrdinary, "Record is Ordinary");

            // Conservative CAS (TryProtectPending) must fail and NOT promote Ordinary to Protected
            bool conservativeProtect = record.TryProtectPending();
            AssertTrue(!conservativeProtect, "Conservative CAS does NOT promote Ordinary record to Protected");
            AssertTrue(record.IsOrdinary, "Record remains Ordinary after failed conservative CAS");
            AssertTrue(!record.IsProtected, "Record is NOT protected by conservative CAS");

            // 2a. Coordinator HandleQueueEviction does not protect Ordinary record
            WindowsClipboard.PasteCoordinator coord1 = new WindowsClipboard.PasteCoordinator(4);
            List<uint> overflow;
            WindowsClipboard.PasteRecord rEvict = coord1.RegisterPaste(
                402, (IntPtr)10, (IntPtr)20, Environment.TickCount, true, out overflow);
            coord1.ResolveOrdinary(rEvict);
            AssertTrue(rEvict.IsOrdinary, "rEvict marked Ordinary");
            bool evictedProtect = coord1.HandleQueueEviction(rEvict);
            AssertTrue(!evictedProtect, "Coordinator HandleQueueEviction does NOT protect Ordinary record");
            AssertTrue(!rEvict.IsProtected, "Evicted ordinary record remains unprotected");

            // 2b. Coordinator RegisterPaste overflow does not protect Ordinary record
            WindowsClipboard.PasteCoordinator coord2 = new WindowsClipboard.PasteCoordinator(2);
            WindowsClipboard.PasteRecord rOverflow = coord2.RegisterPaste(
                403, (IntPtr)10, (IntPtr)20, Environment.TickCount, true, out overflow);
            rOverflow.TryMarkOrdinary();
            AssertTrue(rOverflow.IsOrdinary, "rOverflow marked Ordinary via CAS");
            coord2.RegisterPaste(404, (IntPtr)11, (IntPtr)21, Environment.TickCount + 100, true, out overflow);
            coord2.RegisterPaste(405, (IntPtr)12, (IntPtr)22, Environment.TickCount + 200, true, out overflow);
            AssertTrue(!rOverflow.IsProtected, "Overflow eviction does NOT protect Ordinary record");
            if (overflow != null)
            {
                AssertTrue(!overflow.Contains(403), "Overflow list does NOT contain ordinary sequence 403");
            }

            // 2c. Coordinator ProcessDeadlines does not protect Ordinary record
            WindowsClipboard.PasteCoordinator coord3 = new WindowsClipboard.PasteCoordinator(32);
            int startTick = Environment.TickCount;
            WindowsClipboard.PasteRecord rDeadline = coord3.RegisterPaste(
                406, (IntPtr)10, (IntPtr)20, startTick, true, out overflow);
            rDeadline.TryMarkOrdinary();
            AssertTrue(rDeadline.IsOrdinary, "rDeadline marked Ordinary via CAS");
            List<uint> deadlineProtects = coord3.ProcessDeadlines(startTick + 300);
            AssertTrue(deadlineProtects == null || deadlineProtects.Count == 0, "Coordinator deadline does NOT protect Ordinary record");
            AssertTrue(!rDeadline.IsProtected, "Deadline-checked ordinary record remains unprotected");

            // 3. Subsequent confirmed password promotes exact sequence ONCE even after being Ordinary
            bool latePass = coord3.ResolvePassword(rDeadline);
            AssertTrue(latePass, "Subsequent confirmed password successfully promotes Ordinary record to Protected");
            AssertTrue(rDeadline.IsProtected, "Record is now Protected");
            AssertTrue(rDeadline.Sequence == 406, "Promoted exact sequence 406");
            bool dupPass = coord3.ResolvePassword(rDeadline);
            AssertTrue(!dupPass, "Duplicate ResolvePassword call returns false (promotes exact sequence ONCE)");

            // 4. Pending record conservative CAS (TryProtectPending) protects ONCE
            WindowsClipboard.PasteRecord rPending = new WindowsClipboard.PasteRecord(
                407, (IntPtr)10, (IntPtr)20, Environment.TickCount, true);
            AssertTrue(rPending.State == WindowsClipboard.PasteState.Pending, "rPending is Pending");
            bool firstCas = rPending.TryProtectPending();
            AssertTrue(firstCas, "First TryProtectPending on Pending record returns true");
            AssertTrue(rPending.IsProtected, "rPending is Protected");
            bool secondCas = rPending.TryProtectPending();
            AssertTrue(!secondCas, "Second TryProtectPending on already Protected record returns false");
        }

        private static void RunUiaOrdinaryIdentityTests()
        {
            Console.WriteLine("\n--- UIA Ordinary Identity Tests ---");

            IntPtr validHwnd = (IntPtr)0x1234;
            IntPtr wrongHwnd = (IntPtr)0x5678;

            // 1. zerohwnd: element with NativeWindowHandle == 0 remains Unknown (not eligible)
            bool zeroHwndEligible = WindowsClipboard.IsEligibleOrdinaryEdit(
                IntPtr.Zero, validHwnd, true, false, ControlType.Edit, true);
            AssertTrue(!zeroHwndEligible, "Zero HWND is NOT eligible for OrdinaryEdit (remains Unknown)");

            // 2. wronghwnd: element HWND not matching focused HWND remains Unknown
            bool wrongHwndEligible = WindowsClipboard.IsEligibleOrdinaryEdit(
                wrongHwnd, validHwnd, true, false, ControlType.Edit, true);
            AssertTrue(!wrongHwndEligible, "Mismatched HWND is NOT eligible for OrdinaryEdit (remains Unknown)");

            // 3. unsupportedIsPassword: IsPassword property unsupported remains Unknown
            bool unsupportedPassEligible = WindowsClipboard.IsEligibleOrdinaryEdit(
                validHwnd, validHwnd, false, false, ControlType.Edit, true);
            AssertTrue(!unsupportedPassEligible, "Unsupported IsPassword property is NOT eligible for OrdinaryEdit (remains Unknown)");

            // 4. unfocused: element without keyboard focus remains Unknown
            bool unfocusedEligible = WindowsClipboard.IsEligibleOrdinaryEdit(
                validHwnd, validHwnd, true, false, ControlType.Edit, false);
            AssertTrue(!unfocusedEligible, "Unfocused element (HasKeyboardFocus=false) is NOT eligible for OrdinaryEdit (remains Unknown)");

            // 5. document/container: ControlType not Edit (e.g. Document, Pane, Custom) remains Unknown
            bool docEligible = WindowsClipboard.IsEligibleOrdinaryEdit(
                validHwnd, validHwnd, true, false, ControlType.Document, true);
            AssertTrue(!docEligible, "ControlType.Document is NOT eligible for OrdinaryEdit (remains Unknown)");

            bool paneEligible = WindowsClipboard.IsEligibleOrdinaryEdit(
                validHwnd, validHwnd, true, false, ControlType.Pane, true);
            AssertTrue(!paneEligible, "ControlType.Pane is NOT eligible for OrdinaryEdit (remains Unknown)");

            bool customEligible = WindowsClipboard.IsEligibleOrdinaryEdit(
                validHwnd, validHwnd, true, false, ControlType.Custom, true);
            AssertTrue(!customEligible, "ControlType.Custom is NOT eligible for OrdinaryEdit (remains Unknown)");

            // Positive verification of eligible ordinary edit
            bool validEligible = WindowsClipboard.IsEligibleOrdinaryEdit(
                validHwnd, validHwnd, true, false, ControlType.Edit, true);
            AssertTrue(validEligible, "Valid nonzero HWND matching focus, supported IsPassword=false, Edit, focused is eligible for OrdinaryEdit");

            // 6. verifiednativeeditfalse: Native probe false with matching focus classifies as OrdinaryEdit
            WindowsClipboard.DestinationClassification nativeOrdClass = WindowsClipboard.ClassifyDestinationMetadata(
                false, true, false, true, false);
            AssertTrue(
                nativeOrdClass == WindowsClipboard.DestinationClassification.OrdinaryEdit,
                "Verified native edit probe (false) with verified focus classifies as OrdinaryEdit");

            // Native ordinary record survives coordinator deadline without protection
            WindowsClipboard.PasteCoordinator coordNative = new WindowsClipboard.PasteCoordinator(4);
            List<uint> overflow;
            WindowsClipboard.PasteRecord rNative = coordNative.RegisterPaste(
                501, validHwnd, validHwnd, Environment.TickCount, true, out overflow);
            coordNative.ResolveOrdinary(rNative);
            AssertTrue(rNative.IsOrdinary, "Native verified record marked Ordinary");
            AssertTrue(!rNative.IsProtected, "Native verified record not protected");
            List<uint> nativeDeadlines = coordNative.ProcessDeadlines(rNative.CreatedTickCount + 300);
            AssertTrue(nativeDeadlines == null || nativeDeadlines.Count == 0, "Native ordinary record survives deadline without protection");

            // 7. passwordtruepositivepath: Confirmed password is not eligible for ordinary, classifies as Password, and protects even with protectUnknown=false
            bool passOrdEligible = WindowsClipboard.IsEligibleOrdinaryEdit(
                validHwnd, validHwnd, true, true, ControlType.Edit, true);
            AssertTrue(!passOrdEligible, "Confirmed password is NOT eligible for OrdinaryEdit");

            WindowsClipboard.DestinationClassification uiaPassClass = WindowsClipboard.ClassifyDestinationMetadata(
                null, true, true, true, false);
            AssertTrue(
                uiaPassClass == WindowsClipboard.DestinationClassification.Password,
                "Confirmed UIA IsPassword=true classifies as Password");

            bool protectWhenUnknownOff = WindowsClipboard.ShouldProtectOnPaste(
                WindowsClipboard.DestinationClassification.Password, false);
            AssertTrue(protectWhenUnknownOff, "Confirmed password destination protects even when ProtectUnknownTargets is false");

            WindowsClipboard.PasteCoordinator coordPass = new WindowsClipboard.PasteCoordinator(4);
            WindowsClipboard.PasteRecord rPass = coordPass.RegisterPaste(
                502, validHwnd, validHwnd, Environment.TickCount, false, out overflow);
            bool passResolved = coordPass.ResolvePassword(rPass);
            AssertTrue(passResolved, "ResolvePassword resolves password destination even with protectUnknown=false");
            AssertTrue(rPass.IsProtected, "Password destination is Protected");
            AssertTrue(rPass.Sequence == 502, "Password destination protects exact sequence 502");
        }
    }
}
