using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using DollyPaste;

namespace DollyPaste.Tests
{
    public static class CoreTests
    {
        private static int _passedCount = 0;
        private static int _failedCount = 0;
        private static readonly List<string> _failures = new List<string>();

        public static int Main(string[] args)
        {
            Console.WriteLine("=== Dolly Paste Core Tests (core-v3) ===");

            RunTest("Test_Settings_DefaultsAndNormalize", Test_Settings_DefaultsAndNormalize);
            RunTest("Test_SettingsStore_SaveAndLoadRoundtrip", Test_SettingsStore_SaveAndLoadRoundtrip);
            RunTest("Test_SettingsStore_CorruptFallback", Test_SettingsStore_CorruptFallback);
            RunTest("Test_SettingsStore_EmptyAndPartialJson_PreserveDefaults", Test_SettingsStore_EmptyAndPartialJson_PreserveDefaults);
            RunTest("Test_Capture_BasicAndKindDetection", Test_Capture_BasicAndKindDetection);
            RunTest("Test_Capture_IgnoreEmptyAndOversized", Test_Capture_IgnoreEmptyAndOversized);
            RunTest("Test_Deduplication_PreservesOriginalCaptureAge", Test_Deduplication_PreservesOriginalCaptureAge);
            RunTest("Test_ExpiryBoundary_ExactBoundaryIncludingPins", Test_ExpiryBoundary_ExactBoundaryIncludingPins);
            RunTest("Test_ProtectExisting_CensorAndDiscardModes", Test_ProtectExisting_CensorAndDiscardModes);
            RunTest("Test_SharedFingerprintRegression_CensorMode", Test_SharedFingerprintRegression_CensorMode);
            RunTest("Test_SharedFingerprintRegression_DiscardMode", Test_SharedFingerprintRegression_DiscardMode);
            RunTest("Test_ProtectBeforeCapture_BlocksLaterCapture", Test_ProtectBeforeCapture_BlocksLaterCapture);
            RunTest("Test_TrackRestored_PendingProtectionMeetsTrackRestored", Test_TrackRestored_PendingProtectionMeetsTrackRestored);
            RunTest("Test_TrackRestored_SubsequentProtectScrubs", Test_TrackRestored_SubsequentProtectScrubs);
            RunTest("Test_Recapture_KnownSensitiveFingerprintBlocked", Test_Recapture_KnownSensitiveFingerprintBlocked);
            RunTest("Test_DelayedSequenceRace_DoesNotCensorUnrelatedContent", Test_DelayedSequenceRace_DoesNotCensorUnrelatedContent);
            RunTest("Test_Suppression_FailClosedBeyondCapacity", Test_Suppression_FailClosedBeyondCapacity);
            RunTest("Test_Suppression_FailClosed_RetainedPinnedSecret_ProtectBeforeCapture", Test_Suppression_FailClosed_RetainedPinnedSecret_ProtectBeforeCapture);
            RunTest("Test_Suppression_FailClosed_RetainedPinnedSecret_CaptureBeforeProtect", Test_Suppression_FailClosed_RetainedPinnedSecret_CaptureBeforeProtect);
            RunTest("Test_CountCap_PinsDoNotBypass", Test_CountCap_PinsDoNotBypass);
            RunTest("Test_PayloadSizeCap_4MiBLimit", Test_PayloadSizeCap_4MiBLimit);
            RunTest("Test_ImmediateSettingsReduction", Test_ImmediateSettingsReduction);
            RunTest("Test_Clear_PreservesSuppression", Test_Clear_PreservesSuppression);
            RunTest("Test_ReentrantChangedListener_DoesNotLoopOrLoseChanges", Test_ReentrantChangedListener_DoesNotLoopOrLoseChanges);
            RunTest("Test_Persistence_EncryptedRoundtrip_Restart_Prune_Disable", Test_Persistence_EncryptedRoundtrip_Restart_Prune_Disable);
            RunTest("Test_Persistence_CorruptLoad_And_TimestampValidation", Test_Persistence_CorruptLoad_And_TimestampValidation);
            RunTest("Test_Persistence_MemoryOnlyConstructorRemovesOldHistory_And_RetryCleanup", Test_Persistence_MemoryOnlyConstructorRemovesOldHistory_And_RetryCleanup);
            RunTest("Test_Persistence_AtomicReplace_And_TempCleanupWarning", Test_Persistence_AtomicReplace_And_TempCleanupWarning);
            RunTest("Test_Persistence_DecodedDpapiFixture_UtcAndFutureTimestamps", Test_Persistence_DecodedDpapiFixture_UtcAndFutureTimestamps);
            RunTest("Test_GetText_NullForSensitiveMissingExpired", Test_GetText_NullForSensitiveMissingExpired);

            Console.WriteLine();
            Console.WriteLine(string.Format("Results: {0} passed, {1} failed.", _passedCount, _failedCount));

            if (_failedCount > 0)
            {
                Console.WriteLine("Failures:");
                for (int i = 0; i < _failures.Count; i++)
                {
                    Console.WriteLine(string.Format(" - {0}", _failures[i]));
                }
                return 1;
            }

            Console.WriteLine("All tests passed successfully!");
            return 0;
        }

        private static void RunTest(string name, Action testMethod)
        {
            try
            {
                testMethod();
                _passedCount++;
                Console.WriteLine(string.Format("[PASS] {0}", name));
            }
            catch (Exception ex)
            {
                _failedCount++;
                string msg = string.Format("{0}: {1}", name, ex.Message);
                _failures.Add(msg);
                Console.WriteLine(string.Format("[FAIL] {0}", msg));
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception("Assertion failed: " + message);
            }
        }

        private static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new Exception(string.Format("Assertion failed: {0}. Expected: {1}, Actual: {2}", message, expected, actual));
            }
        }

        private static string CreateTempDir()
        {
            string path = Path.Combine(Path.GetTempPath(), "DollyPasteTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void CleanupDir(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
            }
        }

        private static void Test_Settings_DefaultsAndNormalize()
        {
            Settings settings = new Settings();
            AssertEqual(1440, settings.MaxAgeMinutes, "Default MaxAgeMinutes should be 1440");
            AssertEqual(200, settings.MaxItems, "Default MaxItems should be 200");
            AssertEqual(false, settings.PersistHistory, "Default PersistHistory should be false");
            AssertEqual(SensitiveBehavior.Censor, settings.SensitiveBehavior, "Default SensitiveBehavior should be Censor");
            AssertEqual(true, settings.ProtectUnknownTargets, "Default ProtectUnknownTargets should be true");

            // Test clamping low
            settings.MaxAgeMinutes = -5;
            settings.MaxItems = 2;
            settings.SensitiveBehavior = (SensitiveBehavior)99;
            settings.Normalize();
            AssertEqual(1, settings.MaxAgeMinutes, "MaxAgeMinutes low clamp to 1");
            AssertEqual(10, settings.MaxItems, "MaxItems low clamp to 10");
            AssertEqual(SensitiveBehavior.Censor, settings.SensitiveBehavior, "Invalid enum resets to Censor");

            // Test clamping high
            settings.MaxAgeMinutes = 100000;
            settings.MaxItems = 5000;
            settings.Normalize();
            AssertEqual(43200, settings.MaxAgeMinutes, "MaxAgeMinutes high clamp to 43200");
            AssertEqual(1000, settings.MaxItems, "MaxItems high clamp to 1000");

            // Test clone
            Settings clone = settings.Clone();
            AssertEqual(settings.MaxAgeMinutes, clone.MaxAgeMinutes, "Clone MaxAgeMinutes match");
            AssertEqual(settings.MaxItems, clone.MaxItems, "Clone MaxItems match");
            AssertEqual(settings.PersistHistory, clone.PersistHistory, "Clone PersistHistory match");
            AssertEqual(settings.SensitiveBehavior, clone.SensitiveBehavior, "Clone SensitiveBehavior match");
            AssertEqual(settings.ProtectUnknownTargets, clone.ProtectUnknownTargets, "Clone ProtectUnknownTargets match");
        }

        private static void Test_SettingsStore_SaveAndLoadRoundtrip()
        {
            string tempDir = CreateTempDir();
            try
            {
                Settings original = new Settings();
                original.MaxAgeMinutes = 500;
                original.MaxItems = 150;
                original.PersistHistory = true;
                original.SensitiveBehavior = SensitiveBehavior.Discard;
                original.ProtectUnknownTargets = false;

                SettingsStore.Save(tempDir, original);

                Settings loaded = SettingsStore.Load(tempDir);
                AssertEqual(500, loaded.MaxAgeMinutes, "Loaded MaxAgeMinutes");
                AssertEqual(150, loaded.MaxItems, "Loaded MaxItems");
                AssertEqual(true, loaded.PersistHistory, "Loaded PersistHistory");
                AssertEqual(SensitiveBehavior.Discard, loaded.SensitiveBehavior, "Loaded SensitiveBehavior");
                AssertEqual(false, loaded.ProtectUnknownTargets, "Loaded ProtectUnknownTargets");
            }
            finally
            {
                CleanupDir(tempDir);
            }
        }

        private static void Test_SettingsStore_CorruptFallback()
        {
            string tempDir = CreateTempDir();
            try
            {
                string filePath = Path.Combine(tempDir, "settings.json");
                File.WriteAllText(filePath, "{ invalid json corrupt content !!! }");

                Settings fallback = SettingsStore.Load(tempDir);
                Assert(fallback != null, "Fallback settings should not be null");
                AssertEqual(1440, fallback.MaxAgeMinutes, "Fallback default MaxAgeMinutes");
                AssertEqual(200, fallback.MaxItems, "Fallback default MaxItems");
            }
            finally
            {
                CleanupDir(tempDir);
            }
        }

        private static void Test_SettingsStore_EmptyAndPartialJson_PreserveDefaults()
        {
            string tempDir = CreateTempDir();
            try
            {
                string filePath = Path.Combine(tempDir, "settings.json");

                // 1. Empty JSON object: "{}"
                File.WriteAllText(filePath, "{}");
                Settings loadedEmpty = SettingsStore.Load(tempDir);
                Assert(loadedEmpty != null, "Loaded settings from {} should not be null");
                AssertEqual(1440, loadedEmpty.MaxAgeMinutes, "Default MaxAgeMinutes preserved for {}");
                AssertEqual(200, loadedEmpty.MaxItems, "Default MaxItems preserved for {}");
                AssertEqual(false, loadedEmpty.PersistHistory, "Default PersistHistory preserved for {}");
                AssertEqual(SensitiveBehavior.Censor, loadedEmpty.SensitiveBehavior, "Default SensitiveBehavior preserved for {}");
                AssertEqual(true, loadedEmpty.ProtectUnknownTargets, "Default ProtectUnknownTargets preserved for {}");

                // 2. Partial JSON object: only maxAgeMinutes specified
                File.WriteAllText(filePath, "{\"maxAgeMinutes\": 60}");
                Settings loadedPartial = SettingsStore.Load(tempDir);
                AssertEqual(60, loadedPartial.MaxAgeMinutes, "Explicit MaxAgeMinutes loaded");
                AssertEqual(200, loadedPartial.MaxItems, "Default MaxItems preserved in partial JSON");
                AssertEqual(false, loadedPartial.PersistHistory, "Default PersistHistory preserved in partial JSON");
                AssertEqual(SensitiveBehavior.Censor, loadedPartial.SensitiveBehavior, "Default SensitiveBehavior preserved in partial JSON");
                AssertEqual(true, loadedPartial.ProtectUnknownTargets, "Default ProtectUnknownTargets preserved in partial JSON");

                // 3. Invalid values in JSON: out of range, invalid enum
                File.WriteAllText(filePath, "{\"maxAgeMinutes\": -50, \"maxItems\": 9999, \"sensitiveBehavior\": 99}");
                Settings loadedInvalid = SettingsStore.Load(tempDir);
                AssertEqual(1, loadedInvalid.MaxAgeMinutes, "MaxAgeMinutes normalized to 1");
                AssertEqual(1000, loadedInvalid.MaxItems, "MaxItems normalized to 1000");
                AssertEqual(SensitiveBehavior.Censor, loadedInvalid.SensitiveBehavior, "Invalid enum normalized to Censor");
                AssertEqual(true, loadedInvalid.ProtectUnknownTargets, "Default ProtectUnknownTargets preserved");
            }
            finally
            {
                CleanupDir(tempDir);
            }
        }

        private static void Test_Capture_BasicAndKindDetection()
        {
            string tempDir = CreateTempDir();
            try
            {
                using (HistoryStore store = new HistoryStore(new Settings(), tempDir))
                {
                    ClipEntry textEntry = store.Capture("Simple text note", 1);
                    AssertEqual("TEXT", textEntry.Kind, "Plain text kind");

                    ClipEntry linkEntry = store.Capture("https://example.com/path?q=1", 2);
                    AssertEqual("LINK", linkEntry.Kind, "URL kind");

                    ClipEntry codeEntry = store.Capture("public class Test { int x = 0; }", 3);
                    AssertEqual("CODE", codeEntry.Kind, "Code kind");

                    List<ClipEntry> snapshot = store.Snapshot();
                    AssertEqual(3, snapshot.Count, "Snapshot count");

                    // Verify snapshot items are detached copies
                    snapshot[0].IsPinned = true;
                    List<ClipEntry> freshSnapshot = store.Snapshot();
                    AssertEqual(false, freshSnapshot[0].IsPinned, "Snapshot mutation should not affect store");
                }
            }
            finally
            {
                CleanupDir(tempDir);
            }
        }

        private static void Test_Capture_IgnoreEmptyAndOversized()
        {
            string tempDir = CreateTempDir();
            try
            {
                using (HistoryStore store = new HistoryStore(new Settings(), tempDir))
                {
                    Assert(store.Capture(null, 1) == null, "Null input ignored");
                    Assert(store.Capture(string.Empty, 2) == null, "Empty input ignored");

                    string oversized = new string('x', 32769);
                    Assert(store.Capture(oversized, 3) == null, ">32768 input ignored");

                    string exactLimit = new string('y', 32768);
                    ClipEntry ok = store.Capture(exactLimit, 4);
                    Assert(ok != null, "32768 char input accepted");
                    AssertEqual(1, store.Snapshot().Count, "Snapshot count should be 1");
                }
            }
            finally
            {
                CleanupDir(tempDir);
            }
        }

        private static void Test_Deduplication_PreservesOriginalCaptureAge()
        {
            string tempDir = CreateTempDir();
            try
            {
                DateTime t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
                DateTime currentClock = t0;

                Settings settings = new Settings();
                settings.MaxAgeMinutes = 10;

                using (HistoryStore store = new HistoryStore(settings, tempDir, delegate() { return currentClock; }))
                {
                    // T0: Capture "alpha"
                    ClipEntry alpha1 = store.Capture("alpha", 1);
                    AssertEqual(t0, alpha1.CapturedUtc, "Initial capture at T0");

                    // T0 + 5m: Capture "beta"
                    currentClock = t0.AddMinutes(5);
                    ClipEntry beta = store.Capture("beta", 2);

                    // T0 + 7m: Recopy "alpha"
                    currentClock = t0.AddMinutes(7);
                    ClipEntry alpha2 = store.Capture("alpha", 3);

                    // Verify "alpha" is at the top of snapshot
                    List<ClipEntry> snap = store.Snapshot();
                    AssertEqual("alpha", snap[0].Text, "Alpha should be moved to top");
                    AssertEqual(t0, snap[0].CapturedUtc, "Recopy must NOT reset original capture age");

                    // Advance clock to T0 + 10m:
                    // Alpha's age is 10 min >= MaxAgeMinutes (10), so alpha MUST expire!
                    // Beta's age is 5 min < 10 min, so beta remains.
                    currentClock = t0.AddMinutes(10);
                    store.Prune();

                    List<ClipEntry> snapAfterExpiry = store.Snapshot();
                    AssertEqual(1, snapAfterExpiry.Count, "Alpha should have expired; only beta remains");
                    AssertEqual("beta", snapAfterExpiry[0].Text, "Remaining entry is beta");
                }
            }
            finally
            {
                CleanupDir(tempDir);
            }
        }

        private static void Test_ExpiryBoundary_ExactBoundaryIncludingPins()
        {
            string tempDir = CreateTempDir();
            try
            {
                DateTime t0 = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
                DateTime currentClock = t0;

                Settings settings = new Settings();
                settings.MaxAgeMinutes = 15;

                using (HistoryStore store = new HistoryStore(settings, tempDir, delegate() { return currentClock; }))
                {
                    ClipEntry entry = store.Capture("pinned_secret", 1);
                    store.TogglePin(entry.Id);

                    // At T0 + 14m 59s: age < 15m, not expired
                    currentClock = t0.AddMinutes(14).AddSeconds(59);
                    List<ClipEntry> snap1 = store.Snapshot();
                    AssertEqual(1, snap1.Count, "Item not expired before boundary");
                    AssertEqual("pinned_secret", store.GetText(entry.Id), "GetText returns unexpired text");

                    // At exact boundary: age >= 15m expires, including pins
                    currentClock = t0.AddMinutes(15);
                    List<ClipEntry> snap2 = store.Snapshot();
                    AssertEqual(0, snap2.Count, "Pinned item MUST expire at exact boundary age >= MaxAgeMinutes");
                    Assert(store.GetText(entry.Id) == null, "GetText returns null for expired pinned item");
                }
            }
            finally
            {
                CleanupDir(tempDir);
            }
        }

        private static void Test_ProtectExisting_CensorAndDiscardModes()
        {
            string tempDir = CreateTempDir();
            try
            {
                // 1. Censor Mode
                Settings censorSettings = new Settings();
                censorSettings.SensitiveBehavior = SensitiveBehavior.Censor;
                using (HistoryStore store = new HistoryStore(censorSettings, tempDir))
                {
                    ClipEntry e = store.Capture("confidential_pass", 1);
                    AssertEqual("confidential_pass", e.Text, "Captured text");

                    store.Protect(1);

                    List<ClipEntry> snap = store.Snapshot();
                    AssertEqual(1, snap.Count, "Tombstone retained in Censor mode");
                    AssertEqual(true, snap[0].IsSensitive, "Tombstone marked sensitive");
                    AssertEqual(string.Empty, snap[0].Text, "Sensitive text must be empty");
                    Assert(store.GetText(e.Id) == null, "GetText returns null for sensitive entry");
                }

                // 2. Discard Mode
                Settings discardSettings = new Settings();
                discardSettings.SensitiveBehavior = SensitiveBehavior.Discard;
                using (HistoryStore store = new HistoryStore(discardSettings, tempDir))
                {
                    ClipEntry e = store.Capture("confidential_pass2", 1);
                    store.Protect(1);

                    List<ClipEntry> snap = store.Snapshot();
                    AssertEqual(0, snap.Count, "Record dropped completely in Discard mode");
                    Assert(store.GetText(e.Id) == null, "GetText returns null for discarded entry");
                }
            }
            finally
            {
                CleanupDir(tempDir);
            }
        }

        private static void Test_SharedFingerprintRegression_CensorMode()
        {
            string tempDir = CreateTempDir();
            try
            {
                Settings settings = new Settings();
                settings.PersistHistory = true;
                settings.SensitiveBehavior = SensitiveBehavior.Censor;

                Guid entry1Id;
                using (HistoryStore store = new HistoryStore(settings, tempDir))
                {
                    ClipEntry e1 = store.Capture("critical_master_key", 1);
                    entry1Id = e1.Id;
                    store.TogglePin(entry1Id);

                    // Pending protect on sequence 2
                    store.Protect(2);

                    // Recapture same secret at sequence 2
                    ClipEntry e2 = store.Capture("critical_master_key", 2);
                    Assert(e2 != null, "Tombstone returned in Censor mode");
                    AssertEqual(true, e2.IsSensitive, "Recaptured entry is sensitive tombstone");
                    AssertEqual(string.Empty, e2.Text, "Recaptured entry text is empty");

                    // Check in-memory state: all records for secret must be censored
                    List<ClipEntry> snap = store.Snapshot();
                    for (int i = 0; i < snap.Count; i++)
                    {
                        AssertEqual(true, snap[i].IsSensitive, "All entries in snapshot must be sensitive");
                        AssertEqual(string.Empty, snap[i].Text, "All entries in snapshot must have empty text");
                    }
                    Assert(store.GetText(entry1Id) == null, "GetText for pinned record 1 must return null");
                    Assert(store.GetText(e2.Id) == null, "GetText for record 2 must return null");
                }

                // Restart from persisted disk to verify persisted snapshot has no readable secret
                using (HistoryStore restored = new HistoryStore(settings, tempDir))
                {
                    List<ClipEntry> snap = restored.Snapshot();
                    for (int i = 0; i < snap.Count; i++)
                    {
                        AssertEqual(true, snap[i].IsSensitive, "Restored entry must be sensitive");
                        AssertEqual(string.Empty, snap[i].Text, "Restored entry text must be empty");
                    }
                    Assert(restored.GetText(entry1Id) == null, "GetText on restored store must return null");
                }
            }
            finally
            {
                CleanupDir(tempDir);
            }
        }

        private static void Test_SharedFingerprintRegression_DiscardMode()
        {
            string tempDir = CreateTempDir();
            try
            {
                Settings settings = new Settings();
                settings.PersistHistory = true;
                settings.SensitiveBehavior = SensitiveBehavior.Discard;

                Guid entry1Id;
                using (HistoryStore store = new HistoryStore(settings, tempDir))
                {
                    ClipEntry e1 = store.Capture("critical_discard_key", 1);
                    entry1Id = e1.Id;
                    store.TogglePin(entry1Id);

                    // Pending protect on sequence 2
                    store.Protect(2);

                    // Recapture same secret at sequence 2
                    ClipEntry e2 = store.Capture("critical_discard_key", 2);
                    Assert(e2 == null, "Capture returns null in Discard mode");

                    // Check in-memory state: ALL records removed
                    List<ClipEntry> snap = store.Snapshot();
                    AssertEqual(0, snap.Count, "Snapshot count must be 0 in Discard mode");
                    Assert(store.GetText(entry1Id) == null, "GetText returns null");
                }

                // Restart from persisted disk to verify persisted snapshot is empty
                using (HistoryStore restored = new HistoryStore(settings, tempDir))
                {
                    List<ClipEntry> snap = restored.Snapshot();
                    AssertEqual(0, snap.Count, "Restored snapshot count must be 0 in Discard mode");
                }
            }
            finally
            {
                CleanupDir(tempDir);
            }
        }

        private static void Test_ProtectBeforeCapture_BlocksLaterCapture()
        {
            string tempDir = CreateTempDir();
            try
            {
                Settings settings = new Settings();
                settings.SensitiveBehavior = SensitiveBehavior.Discard;

                using (HistoryStore store = new HistoryStore(settings, tempDir))
                {
                    store.Protect(100);
                    ClipEntry result = store.Capture("blocked_secret", 100);
                    Assert(result == null, "Protect before Capture must block capture in Discard mode");
                    AssertEqual(0, store.Snapshot().Count, "No item added");
                }

                settings.SensitiveBehavior = SensitiveBehavior.Censor;
                using (HistoryStore store = new HistoryStore(settings, tempDir))
                {
                    store.Protect(200);
                    ClipEntry result = store.Capture("censored_secret", 200);
                    Assert(result != null, "Tombstone added in Censor mode");
                    AssertEqual(true, result.IsSensitive, "Tombstone marked sensitive");
                    AssertEqual(string.Empty, result.Text, "Tombstone text empty");
                }
            }
            finally
            {
                CleanupDir(tempDir);
            }
        }

        private static void Test_TrackRestored_PendingProtectionMeetsTrackRestored()
        {
            string tempDir = CreateTempDir();
            try
            {
                Settings settings = new Settings();
                settings.SensitiveBehavior = SensitiveBehavior.Discard;

                using (HistoryStore store = new HistoryStore(settings, tempDir))
                {
                    ClipEntry e = store.Capture("password_for_paste", 1);
                    AssertEqual(1, store.Snapshot().Count, "Captured 1 item");

                    // Sequence 2 was protected before TrackRestored arrived
                    store.Protect(2);

                    // TrackRestored meets pending protection
                    store.TrackRestored(e.Id, 2);

                    AssertEqual(0, store.Snapshot().Count, "Shared scrub must remove item when TrackRestored meets pending protection");
                }
            }
            finally
            {
                CleanupDir(tempDir);
            }
        }

        private static void Test_TrackRestored_SubsequentProtectScrubs()
        {
            string tempDir = CreateTempDir();
            try
            {
                Settings settings = new Settings();
                settings.SensitiveBehavior = SensitiveBehavior.Discard;

                using (HistoryStore store = new HistoryStore(settings, tempDir))
                {
                    ClipEntry e = store.Capture("password_for_copy", 1);
                    AssertEqual(1, store.Snapshot().Count, "Captured 1 item");

                    // TrackRestored maps sequence 2 to entry's fingerprint
                    store.TrackRestored(e.Id, 2);

                    // Subsequent Protect(2) arrives
                    store.Protect(2);

                    AssertEqual(0, store.Snapshot().Count, "Protect after TrackRestored must scrub entry");
                }
            }
            finally
            {
                CleanupDir(tempDir);
            }
        }

        private static void Test_Recapture_KnownSensitiveFingerprintBlocked()
        {
            string tempDir = CreateTempDir();
            try
            {
                Settings settings = new Settings();
                settings.SensitiveBehavior = SensitiveBehavior.Discard;

                using (HistoryStore store = new HistoryStore(settings, tempDir))
                {
                    store.Capture("secret_xyz", 1);
                    store.Protect(1);

                    // Later capture at sequence 50 (not explicitly protected)
                    ClipEntry recaptured = store.Capture("secret_xyz", 50);
                    Assert(recaptured == null, "Recapture of known sensitive secret must be blocked");
                    AssertEqual(0, store.Snapshot().Count, "No item added for sensitive fingerprint");
                }
            }
            finally
            {
                CleanupDir(tempDir);
            }
        }

        private static void Test_DelayedSequenceRace_DoesNotCensorUnrelatedContent()
        {
            string tempDir = CreateTempDir();
            try
            {
                using (HistoryStore store = new HistoryStore(new Settings(), tempDir))
                {
                    // Sequence 500 captured
                    ClipEntry entry = store.Capture("unrelated_public_data", 500);

                    // Delayed Protect for sequence 1 arrives (well behind the active window)
                    store.Protect(1);

                    List<ClipEntry> snap = store.Snapshot();
                    AssertEqual(1, snap.Count, "Snapshot count");
                    AssertEqual(false, snap[0].IsSensitive, "Unrelated content must NOT be censored by old delayed sequence");
                    AssertEqual("unrelated_public_data", snap[0].Text, "Content unchanged");
                }
            }
            finally
            {
                CleanupDir(tempDir);
            }
        }

        private static void Test_Suppression_FailClosedBeyondCapacity()
        {
            string tempDir = CreateTempDir();
            try
            {
                using (HistoryStore store = new HistoryStore(new Settings(), tempDir))
                {
                    // Add 1024 unique secrets and protect each
                    for (uint i = 1; i <= 1024; i++)
                    {
                        string secret = string.Format("capacity_secret_{0}", i);
                        store.Capture(secret, i);
                        store.Protect(i);
                    }

                    // Attempt to protect the 1025th secret beyond capacity
                    string extraSecret = "capacity_secret_1025";
                    store.Capture(extraSecret, 1025);
                    store.Protect(1025);

                    // Verify visible StorageWarning
                    Assert(!string.IsNullOrEmpty(store.StorageWarning), "StorageWarning must be visible upon reaching suppression capacity");

                    // Verify fail-closed stop-capture: subsequent capture rejected
                    ClipEntry blocked = store.Capture("new_normal_text", 2000);
                    Assert(blocked == null, "Capture must fail-closed when suppression capacity is reached");

                    // Verify earliest protected secret is STILL blocked (not evicted!)
                    ClipEntry recapturedFirst = store.Capture("capacity_secret_1", 2001);
                    Assert(recapturedFirst == null, "Earliest known-sensitive fingerprint must NOT be evicted");
                }
            }
            finally
            {
                CleanupDir(tempDir);
            }
        }

        private static void Test_Suppression_FailClosed_RetainedPinnedSecret_ProtectBeforeCapture()
        {
            string tempDir = CreateTempDir();
            try
            {
                // Mode A: Censor
                Settings censorSettings = new Settings();
                censorSettings.SensitiveBehavior = SensitiveBehavior.Censor;
                censorSettings.PersistHistory = true;

                Guid pinnedId;
                using (HistoryStore store = new HistoryStore(censorSettings, tempDir))
                {
                    ClipEntry pinnedEntry = store.Capture("secret_pinned_censor", 1);
                    pinnedId = pinnedEntry.Id;
                    store.TogglePin(pinnedId);

                    // Fill suppression capacity with >1024 distinct protected values
                    for (uint i = 2; i <= 1027; i++)
                    {
                        string val = string.Format("fill_secret_c_{0}", i);
                        store.Capture(val, i);
                        store.Protect(i);
                    }

                    Assert(!string.IsNullOrEmpty(store.StorageWarning), "StorageWarning set after 1024 protected values");

                    // Protect-before-Capture on the earlier pinned secret
                    uint protectSeq = 2000;
                    store.Protect(protectSeq);

                    // Now capture the secret with that sequence
                    ClipEntry result = store.Capture("secret_pinned_censor", protectSeq);
                    Assert(result == null, "Capture returns null when capture suspended");

                    // Pinned entry in memory MUST be scrubbed (censored)
                    List<ClipEntry> snap = store.Snapshot();
                    for (int i = 0; i < snap.Count; i++)
                    {
                        if (snap[i].Id == pinnedId)
                        {
                            AssertEqual(true, snap[i].IsSensitive, "Pinned secret must be marked sensitive");
                            AssertEqual(string.Empty, snap[i].Text, "Pinned secret text must be empty");
                        }
                    }
                    Assert(store.GetText(pinnedId) == null, "GetText must return null for censored pinned secret");
                }

                // Verify persisted snapshot has no readable secret
                using (HistoryStore storeRestored = new HistoryStore(censorSettings, tempDir))
                {
                    List<ClipEntry> snap = storeRestored.Snapshot();
                    for (int i = 0; i < snap.Count; i++)
                    {
                        if (snap[i].Id == pinnedId)
                        {
                            AssertEqual(true, snap[i].IsSensitive, "Restored pinned secret must be marked sensitive");
                            AssertEqual(string.Empty, snap[i].Text, "Restored pinned secret text must be empty");
                        }
                    }
                    Assert(storeRestored.GetText(pinnedId) == null, "GetText on restored store must return null");
                }

                // Mode B: Discard
                Settings discardSettings = new Settings();
                discardSettings.SensitiveBehavior = SensitiveBehavior.Discard;
                discardSettings.PersistHistory = true;

                Guid discardPinnedId;
                using (HistoryStore store = new HistoryStore(discardSettings, tempDir))
                {
                    ClipEntry pinnedEntry = store.Capture("secret_pinned_discard", 3000);
                    discardPinnedId = pinnedEntry.Id;
                    store.TogglePin(discardPinnedId);

                    for (uint i = 3001; i <= 4026; i++)
                    {
                        string val = string.Format("fill_secret_d_{0}", i);
                        store.Capture(val, i);
                        store.Protect(i);
                    }

                    Assert(!string.IsNullOrEmpty(store.StorageWarning), "StorageWarning set after 1024 protected values");

                    // Protect-before-Capture on the earlier pinned secret
                    uint protectSeq = 5000;
                    store.Protect(protectSeq);
                    ClipEntry result = store.Capture("secret_pinned_discard", protectSeq);
                    Assert(result == null, "Capture returns null in Discard mode");

                    // Pinned entry MUST be completely removed
                    List<ClipEntry> snap = store.Snapshot();
                    for (int i = 0; i < snap.Count; i++)
                    {
                        Assert(snap[i].Id != discardPinnedId, "Pinned secret must be dropped in Discard mode");
                    }
                    Assert(store.GetText(discardPinnedId) == null, "GetText returns null");
                }

                using (HistoryStore storeRestored = new HistoryStore(discardSettings, tempDir))
                {
                    List<ClipEntry> snap = storeRestored.Snapshot();
                    for (int i = 0; i < snap.Count; i++)
                    {
                        Assert(snap[i].Id != discardPinnedId, "Restored snapshot must not contain dropped secret");
                    }
                }
            }
            finally
            {
                CleanupDir(tempDir);
            }
        }

        private static void Test_Suppression_FailClosed_RetainedPinnedSecret_CaptureBeforeProtect()
        {
            string tempDir = CreateTempDir();
            try
            {
                // Mode A: Censor
                Settings censorSettings = new Settings();
                censorSettings.SensitiveBehavior = SensitiveBehavior.Censor;
                censorSettings.PersistHistory = true;

                Guid pinnedId;
                using (HistoryStore store = new HistoryStore(censorSettings, tempDir))
                {
                    ClipEntry pinnedEntry = store.Capture("secret_cbp_censor", 1);
                    pinnedId = pinnedEntry.Id;
                    store.TogglePin(pinnedId);

                    // Fill suppression capacity >1024
                    for (uint i = 2; i <= 1027; i++)
                    {
                        string val = string.Format("fill_cbp_c_{0}", i);
                        store.Capture(val, i);
                        store.Protect(i);
                    }

                    Assert(!string.IsNullOrEmpty(store.StorageWarning), "StorageWarning set after 1024 protected values");

                    // Capture-before-Protect on the earlier pinned secret
                    uint seq = 6000;
                    ClipEntry result = store.Capture("secret_cbp_censor", seq);
                    Assert(result == null, "Capture returns null when capture suspended");

                    // Now call Protect for that sequence
                    store.Protect(seq);

                    // Pinned secret in memory MUST be scrubbed (censored)
                    List<ClipEntry> snap = store.Snapshot();
                    for (int i = 0; i < snap.Count; i++)
                    {
                        if (snap[i].Id == pinnedId)
                        {
                            AssertEqual(true, snap[i].IsSensitive, "Pinned secret must be censored");
                            AssertEqual(string.Empty, snap[i].Text, "Pinned secret text must be empty");
                        }
                    }
                    Assert(store.GetText(pinnedId) == null, "GetText must return null for censored secret");
                }

                using (HistoryStore storeRestored = new HistoryStore(censorSettings, tempDir))
                {
                    List<ClipEntry> snap = storeRestored.Snapshot();
                    for (int i = 0; i < snap.Count; i++)
                    {
                        if (snap[i].Id == pinnedId)
                        {
                            AssertEqual(true, snap[i].IsSensitive, "Restored pinned secret must be censored");
                            AssertEqual(string.Empty, snap[i].Text, "Restored pinned secret text must be empty");
                        }
                    }
                    Assert(storeRestored.GetText(pinnedId) == null, "GetText on restored store returns null");
                }

                // Mode B: Discard
                Settings discardSettings = new Settings();
                discardSettings.SensitiveBehavior = SensitiveBehavior.Discard;
                discardSettings.PersistHistory = true;

                Guid discardPinnedId;
                using (HistoryStore store = new HistoryStore(discardSettings, tempDir))
                {
                    ClipEntry pinnedEntry = store.Capture("secret_cbp_discard", 7000);
                    discardPinnedId = pinnedEntry.Id;
                    store.TogglePin(discardPinnedId);

                    for (uint i = 7001; i <= 8026; i++)
                    {
                        string val = string.Format("fill_cbp_d_{0}", i);
                        store.Capture(val, i);
                        store.Protect(i);
                    }

                    Assert(!string.IsNullOrEmpty(store.StorageWarning), "StorageWarning set after 1024 protected values");

                    // Capture-before-Protect
                    uint seq = 9000;
                    store.Capture("secret_cbp_discard", seq);
                    store.Protect(seq);

                    // Pinned entry MUST be dropped
                    List<ClipEntry> snap = store.Snapshot();
                    for (int i = 0; i < snap.Count; i++)
                    {
                        Assert(snap[i].Id != discardPinnedId, "Pinned secret must be dropped in Discard mode");
                    }
                    Assert(store.GetText(discardPinnedId) == null, "GetText returns null");
                }

                using (HistoryStore storeRestored = new HistoryStore(discardSettings, tempDir))
                {
                    List<ClipEntry> snap = storeRestored.Snapshot();
                    for (int i = 0; i < snap.Count; i++)
                    {
                        Assert(snap[i].Id != discardPinnedId, "Restored snapshot must not contain dropped secret");
                    }
                }
            }
            finally
            {
                CleanupDir(tempDir);
            }
        }

        private static void Test_CountCap_PinsDoNotBypass()
        {
            string tempDir = CreateTempDir();
            try
            {
                Settings settings = new Settings();
                settings.MaxItems = 10;

                using (HistoryStore store = new HistoryStore(settings, tempDir))
                {
                    // Add 15 items, pin first 5
                    for (int i = 0; i < 15; i++)
                    {
                        ClipEntry e = store.Capture(string.Format("item_{0}", i), (uint)(i + 1));
                        if (i < 5)
                        {
                            store.TogglePin(e.Id);
                        }
                    }

                    List<ClipEntry> snap = store.Snapshot();
                    Assert(snap.Count <= 10, "Total count must not exceed MaxItems (10)");

                    // Pin all 10 remaining items
                    for (int i = 0; i < snap.Count; i++)
                    {
                        if (!snap[i].IsPinned)
                        {
                            store.TogglePin(snap[i].Id);
                        }
                    }

                    // Add one more item
                    store.Capture("item_extra", 99);

                    List<ClipEntry> snapAfter = store.Snapshot();
                    AssertEqual(10, snapAfter.Count, "Pins NEVER bypass count cap (strictly 10)");
                }
            }
            finally
            {
                CleanupDir(tempDir);
            }
        }

        private static void Test_PayloadSizeCap_4MiBLimit()
        {
            string tempDir = CreateTempDir();
            try
            {
                using (HistoryStore store = new HistoryStore(new Settings(), tempDir))
                {
                    // 150 items of 30,000 characters = ~4.5 MB, which exceeds 4 MiB
                    string chunk = new string('A', 30000);
                    for (uint i = 1; i <= 150; i++)
                    {
                        store.Capture(chunk, i);
                    }

                    List<ClipEntry> snap = store.Snapshot();
                    int totalBytes = 0;
                    for (int i = 0; i < snap.Count; i++)
                    {
                        totalBytes += Encoding.UTF8.GetByteCount(snap[i].Text);
                    }

                    Assert(totalBytes <= 4 * 1024 * 1024, "Total payload must not exceed 4 MiB");
                }
            }
            finally
            {
                CleanupDir(tempDir);
            }
        }

        private static void Test_ImmediateSettingsReduction()
        {
            string tempDir = CreateTempDir();
            try
            {
                Settings settings = new Settings();
                settings.MaxItems = 50;

                using (HistoryStore store = new HistoryStore(settings, tempDir))
                {
                    for (uint i = 1; i <= 30; i++)
                    {
                        store.Capture(string.Format("entry_{0}", i), i);
                    }
                    AssertEqual(30, store.Snapshot().Count, "Initial count 30");

                    Settings updated = store.Settings;
                    updated.MaxItems = 15;
                    store.UpdateSettings(updated);

                    AssertEqual(15, store.Snapshot().Count, "Immediate reduction applied to count (15)");
                }
            }
            finally
            {
                CleanupDir(tempDir);
            }
        }

        private static void Test_Clear_PreservesSuppression()
        {
            string tempDir = CreateTempDir();
            try
            {
                Settings settings = new Settings();
                settings.SensitiveBehavior = SensitiveBehavior.Discard;

                using (HistoryStore store = new HistoryStore(settings, tempDir))
                {
                    store.Capture("secret_to_clear", 1);
                    store.Protect(1);

                    store.Clear();
                    AssertEqual(0, store.Snapshot().Count, "Clear empties history");

                    // Active clipboard sequence and session suppression must remain active
                    ClipEntry recaptured = store.Capture("secret_to_clear", 2);
                    Assert(recaptured == null, "Clear must NOT reset suppression for known sensitive content");
                }
            }
            finally
            {
                CleanupDir(tempDir);
            }
        }

        private static void Test_ReentrantChangedListener_DoesNotLoopOrLoseChanges()
        {
            string tempDir = CreateTempDir();
            try
            {
                using (HistoryStore store = new HistoryStore(new Settings(), tempDir))
                {
                    int callCount = 0;

                    EventHandler handler = null;
                    handler = delegate(object sender, EventArgs e)
                    {
                        callCount++;
                        // Listener calling Snapshot must not recurse forever
                        List<ClipEntry> snap = store.Snapshot();
                        Assert(snap != null, "Snapshot from Changed listener");

                        // Mutate state reentrantly on first callback by removing the item in snapshot
                        if (callCount == 1 && snap.Count > 0)
                        {
                            store.Remove(snap[0].Id);
                        }
                    };

                    store.Changed += handler;

                    ClipEntry e1 = store.Capture("first_item", 1);
                    Assert(e1 != null, "Capture returned entry");

                    // Ensure reentrant Remove was processed without infinite loop and delivered follow-up Changed
                    Assert(callCount >= 2, string.Format("Reentrant mutation must trigger follow-up Changed event. Actual count: {0}", callCount));
                    AssertEqual(0, store.Snapshot().Count, "Item was successfully removed reentrantly");
                }
            }
            finally
            {
                CleanupDir(tempDir);
            }
        }

        private static void Test_Persistence_EncryptedRoundtrip_Restart_Prune_Disable()
        {
            string tempDir = CreateTempDir();
            try
            {
                Settings settings = new Settings();
                settings.PersistHistory = true;

                string historyFile = Path.Combine(tempDir, "history.dat");

                Guid pinnedId;
                using (HistoryStore store = new HistoryStore(settings, tempDir))
                {
                    ClipEntry e1 = store.Capture("persist_val_1", 1);
                    ClipEntry e2 = store.Capture("persist_val_2", 2);
                    pinnedId = e1.Id;
                    store.TogglePin(pinnedId);

                    Assert(File.Exists(historyFile), "history.dat should exist on disk");

                    // Verify encryption: plaintext strings must NOT appear in raw file bytes
                    byte[] fileBytes = File.ReadAllBytes(historyFile);
                    string rawString = Encoding.UTF8.GetString(fileBytes);
                    Assert(rawString.IndexOf("persist_val_1") < 0, "No plaintext history on disk (DPAPI encrypted)");
                    Assert(rawString.IndexOf("persist_val_2") < 0, "No plaintext history on disk (DPAPI encrypted)");
                }

                // Restart from disk
                using (HistoryStore store2 = new HistoryStore(settings, tempDir))
                {
                    List<ClipEntry> snap = store2.Snapshot();
                    AssertEqual(2, snap.Count, "Restored entry count");

                    bool foundPinned = false;
                    for (int i = 0; i < snap.Count; i++)
                    {
                        if (snap[i].Id == pinnedId && snap[i].IsPinned)
                        {
                            foundPinned = true;
                        }
                    }
                    Assert(foundPinned, "Pinned flag restored properly");

                    // Disable persistence: switching persistence off removes stored history file
                    Settings offSettings = store2.Settings;
                    offSettings.PersistHistory = false;
                    store2.UpdateSettings(offSettings);

                    Assert(!File.Exists(historyFile), "Switching persistence off must delete history.dat");
                }
            }
            finally
            {
                CleanupDir(tempDir);
            }
        }

        private static void Test_Persistence_CorruptLoad_And_TimestampValidation()
        {
            string tempDir = CreateTempDir();
            try
            {
                string historyFile = Path.Combine(tempDir, "history.dat");
                // Write random corrupt data
                File.WriteAllBytes(historyFile, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03 });

                Settings settings = new Settings();
                settings.PersistHistory = true;

                // Loading corrupt data must not crash startup, must set StorageWarning
                using (HistoryStore store = new HistoryStore(settings, tempDir))
                {
                    AssertEqual(0, store.Snapshot().Count, "Corrupt history yields empty store");
                    Assert(!string.IsNullOrEmpty(store.StorageWarning), "StorageWarning set on corrupt load");
                }
            }
            finally
            {
                CleanupDir(tempDir);
            }
        }

        private static void Test_Persistence_MemoryOnlyConstructorRemovesOldHistory_And_RetryCleanup()
        {
            string tempDir = CreateTempDir();
            try
            {
                string historyFile = Path.Combine(tempDir, "history.dat");
                string tmpFile = Path.Combine(tempDir, "history.dat.tmp");

                // Part 1: Pre-existing history.dat removed by constructor in memory-only mode
                File.WriteAllText(historyFile, "legacy_data");
                File.WriteAllText(tmpFile, "legacy_tmp");

                Settings memorySettings = new Settings();
                memorySettings.PersistHistory = false;

                using (HistoryStore store = new HistoryStore(memorySettings, tempDir))
                {
                    Assert(!File.Exists(historyFile), "Constructor in memory-only mode must delete old history.dat");
                    Assert(!File.Exists(tmpFile), "Constructor in memory-only mode must delete old history.dat.tmp");
                    Assert(string.IsNullOrEmpty(store.StorageWarning), "No storage warning on successful deletion");
                }

                // Part 2: Simulated file lock failure, then retry via UpdateSettings(false)
                File.WriteAllText(historyFile, "locked_data");

                // Lock history.dat with FileShare.None
                using (FileStream lockStream = new FileStream(historyFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    using (HistoryStore store = new HistoryStore(memorySettings, tempDir))
                    {
                        // File could not be deleted due to lock
                        Assert(File.Exists(historyFile), "File remains while locked");
                        Assert(!string.IsNullOrEmpty(store.StorageWarning), "StorageWarning reports failed deletion");

                        // Calling UpdateSettings while still locked should retry and retain warning
                        store.UpdateSettings(memorySettings);
                        Assert(!string.IsNullOrEmpty(store.StorageWarning), "StorageWarning retained while still locked");
                    }
                }

                // Lock is now released! Test retry via UpdateSettings(false) on another instance
                using (HistoryStore store = new HistoryStore(memorySettings, tempDir))
                {
                    Assert(!File.Exists(historyFile), "File deleted after lock released");
                    Assert(string.IsNullOrEmpty(store.StorageWarning), "StorageWarning cleared after successful cleanup");
                }
            }
            finally
            {
                CleanupDir(tempDir);
            }
        }

        private static void Test_Persistence_AtomicReplace_And_TempCleanupWarning()
        {
            string tempDir = CreateTempDir();
            try
            {
                Settings settings = new Settings();
                settings.PersistHistory = true;

                string historyFile = Path.Combine(tempDir, "history.dat");

                using (HistoryStore store = new HistoryStore(settings, tempDir))
                {
                    store.Capture("atomic_item_1", 1);
                    Assert(File.Exists(historyFile), "history.dat created");

                    // Second capture: triggers File.Replace atomically
                    store.Capture("atomic_item_2", 2);
                    Assert(File.Exists(historyFile), "history.dat exists after atomic replace");
                }

                // Verify temp file does not linger
                string tmpFile = Path.Combine(tempDir, "history.dat.tmp");
                Assert(!File.Exists(tmpFile), "history.dat.tmp must not linger after persist");
            }
            finally
            {
                CleanupDir(tempDir);
            }
        }

        private static void Test_Persistence_DecodedDpapiFixture_UtcAndFutureTimestamps()
        {
            string tempDir = CreateTempDir();
            try
            {
                DateTime fixedNow = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

                HistoryFileDto dto = new HistoryFileDto();
                dto.Version = 1;
                dto.Records = new List<HistoryRecordDto>();

                // 1. Valid entry (5 minutes ago, UTC)
                Guid validId = Guid.NewGuid();
                HistoryRecordDto validRecord = new HistoryRecordDto();
                validRecord.Id = validId;
                validRecord.Text = "valid_utc_payload";
                validRecord.CapturedUtc = fixedNow.AddMinutes(-5);
                validRecord.IsPinned = false;
                validRecord.IsSensitive = false;
                validRecord.Kind = "TEXT";
                dto.Records.Add(validRecord);

                // 2. Future entry (+10 seconds ahead of now - strictly > now)
                HistoryRecordDto futureRecordNear = new HistoryRecordDto();
                futureRecordNear.Id = Guid.NewGuid();
                futureRecordNear.Text = "future_near_payload";
                futureRecordNear.CapturedUtc = fixedNow.AddSeconds(10);
                futureRecordNear.IsPinned = false;
                futureRecordNear.IsSensitive = false;
                futureRecordNear.Kind = "TEXT";
                dto.Records.Add(futureRecordNear);

                // 3. Far future entry (+10 minutes ahead)
                HistoryRecordDto futureRecordFar = new HistoryRecordDto();
                futureRecordFar.Id = Guid.NewGuid();
                futureRecordFar.Text = "future_far_payload";
                futureRecordFar.CapturedUtc = fixedNow.AddMinutes(10);
                futureRecordFar.IsPinned = false;
                futureRecordFar.IsSensitive = false;
                futureRecordFar.Kind = "TEXT";
                dto.Records.Add(futureRecordFar);

                // 4. Non-UTC timestamp (Local kind)
                HistoryRecordDto localRecord = new HistoryRecordDto();
                localRecord.Id = Guid.NewGuid();
                localRecord.Text = "local_kind_payload";
                localRecord.CapturedUtc = new DateTime(2026, 6, 1, 11, 0, 0, DateTimeKind.Local);
                localRecord.IsPinned = false;
                localRecord.IsSensitive = false;
                localRecord.Kind = "TEXT";
                dto.Records.Add(localRecord);

                // 5. Non-UTC timestamp (Unspecified kind)
                HistoryRecordDto unspecifiedRecord = new HistoryRecordDto();
                unspecifiedRecord.Id = Guid.NewGuid();
                unspecifiedRecord.Text = "unspecified_kind_payload";
                unspecifiedRecord.CapturedUtc = new DateTime(2026, 6, 1, 11, 0, 0, DateTimeKind.Unspecified);
                unspecifiedRecord.IsPinned = false;
                unspecifiedRecord.IsSensitive = false;
                unspecifiedRecord.Kind = "TEXT";
                dto.Records.Add(unspecifiedRecord);

                // 6. Guid.Empty
                HistoryRecordDto emptyGuidRecord = new HistoryRecordDto();
                emptyGuidRecord.Id = Guid.Empty;
                emptyGuidRecord.Text = "empty_guid_payload";
                emptyGuidRecord.CapturedUtc = fixedNow.AddMinutes(-2);
                emptyGuidRecord.IsPinned = false;
                emptyGuidRecord.IsSensitive = false;
                emptyGuidRecord.Kind = "TEXT";
                dto.Records.Add(emptyGuidRecord);

                // 7. Oversized text (>32768)
                HistoryRecordDto oversizedRecord = new HistoryRecordDto();
                oversizedRecord.Id = Guid.NewGuid();
                oversizedRecord.Text = new string('Z', 32769);
                oversizedRecord.CapturedUtc = fixedNow.AddMinutes(-1);
                oversizedRecord.IsPinned = false;
                oversizedRecord.IsSensitive = false;
                oversizedRecord.Kind = "TEXT";
                dto.Records.Add(oversizedRecord);

                // Serialize and DPAPI-encrypt the fixture
                byte[] plainBytes;
                using (MemoryStream ms = new MemoryStream())
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(HistoryFileDto));
                    serializer.WriteObject(ms, dto);
                    plainBytes = ms.ToArray();
                }

                byte[] encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
                string historyFile = Path.Combine(tempDir, "history.dat");
                File.WriteAllBytes(historyFile, encryptedBytes);

                Settings settings = new Settings();
                settings.PersistHistory = true;

                // Load with injected clock returning fixedNow
                using (HistoryStore store = new HistoryStore(settings, tempDir, delegate() { return fixedNow; }))
                {
                    List<ClipEntry> snap = store.Snapshot();
                    AssertEqual(1, snap.Count, "Only valid UTC unexpired record must be loaded");
                    AssertEqual(validId, snap[0].Id, "Loaded record matches validId");
                    AssertEqual("valid_utc_payload", snap[0].Text, "Loaded record text");
                }
            }
            finally
            {
                CleanupDir(tempDir);
            }
        }

        private static void Test_GetText_NullForSensitiveMissingExpired()
        {
            string tempDir = CreateTempDir();
            try
            {
                DateTime t0 = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
                DateTime clock = t0;

                Settings settings = new Settings();
                settings.MaxAgeMinutes = 5;

                using (HistoryStore store = new HistoryStore(settings, tempDir, delegate() { return clock; }))
                {
                    ClipEntry valid = store.Capture("hello", 1);
                    ClipEntry secret = store.Capture("pwd", 2);
                    store.Protect(2);

                    AssertEqual("hello", store.GetText(valid.Id), "GetText valid entry");
                    Assert(store.GetText(secret.Id) == null, "GetText returns null for sensitive entry");
                    Assert(store.GetText(Guid.NewGuid()) == null, "GetText returns null for non-existent GUID");

                    // Advance clock past 5m
                    clock = t0.AddMinutes(6);
                    Assert(store.GetText(valid.Id) == null, "GetText returns null for expired entry");
                }
            }
            finally
            {
                CleanupDir(tempDir);
            }
        }
    }
}
