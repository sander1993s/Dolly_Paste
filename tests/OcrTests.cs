using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using DollyPaste;

namespace DollyPaste.Tests
{
    public static class OcrTests
    {
        private sealed class FakeEngine : IOcrEngine
        {
            internal Func<string, CancellationToken, Task<OcrResult>> Handler;
            public Task<OcrResult> RecognizeAsync(string pngBase64, CancellationToken token)
            {
                return Handler(pngBase64, token);
            }
        }

        private static int _failures;

        public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--native-smoke") return NativeSmoke();
            Run("Results preserve lines, remove empty text and bound text safely", Results);
            Run("Running plus queued capacity and duplicate IDs are bounded", CapacityAndDeduplication);
            Run("UTF16 payload byte budget bounds the queue", ByteBudget);
            Run("Cancelling a queued image releases its slot and never runs it", CancelQueued);
            Run("Cancelling an active image suppresses its result and allows replacement", CancelActiveAndReplace);
            Run("Disposal cancels active work, discards queued images and suppresses delivery", Disposal);
            Run("Synchronous cancellation completion cannot invalidate disposal", SynchronousCancellation);
            Run("Engine errors and no-text/unavailable results do not stall later jobs", FailureAndStatus);
            Run("Callback failure does not stop subsequent OCR jobs", CallbackFailure);
            Console.WriteLine("OCR service tests: " + (9 - _failures) + " passed, " + _failures + " failed.");
            return _failures == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            try { test(); Console.WriteLine("[PASS] " + name); }
            catch (Exception ex) { _failures++; Console.WriteLine("[FAIL] " + name + ": " + ex.Message); }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static void Wait(ManualResetEventSlim signal, string message)
        {
            Assert(signal.Wait(TimeSpan.FromSeconds(5)), message);
        }

        private static Task<OcrResult> Success(string text)
        {
            return Task.FromResult(new OcrResult(OcrResultStatus.Completed, text));
        }

        private static void Results()
        {
            Assert(new OcrResult(OcrResultStatus.Completed, " \r\n\t ").Status == OcrResultStatus.NoText, "Whitespace is no text.");
            OcrResult lines = new OcrResult(OcrResultStatus.Completed, " first\r\nsecond\0 ");
            Assert(lines.Text == "first\r\nsecond", "Line boundaries must be preserved.");
            OcrResult bounded = new OcrResult(OcrResultStatus.Completed, new string('a', OcrResult.MaxTextLength - 1) + "\ud83d\ude00tail");
            Assert(bounded.Text.Length == OcrResult.MaxTextLength - 1, "Truncation must not split surrogate pairs.");
            OcrResult exact = new OcrResult(OcrResultStatus.Completed, new string('a', OcrResult.MaxTextLength - 1) + "\ud83d");
            Assert(exact.Text.Length == OcrResult.MaxTextLength - 1, "An engine-truncated high surrogate at exactly the limit must be removed.");
            Assert(new OcrResult(OcrResultStatus.Failed, "secret").Text == null, "Failure results must not retain text.");
        }

        private static void CapacityAndDeduplication()
        {
            ManualResetEventSlim started = new ManualResetEventSlim();
            ManualResetEventSlim finished = new ManualResetEventSlim();
            TaskCompletionSource<OcrResult> first = new TaskCompletionSource<OcrResult>();
            int calls = 0;
            int delivered = 0;
            Guid one = Guid.NewGuid(), two = Guid.NewGuid(), three = Guid.NewGuid();
            bool callbackEnqueued = false;
            OcrService service = null;
            FakeEngine engine = new FakeEngine { Handler = delegate(string value, CancellationToken token)
            {
                int call = Interlocked.Increment(ref calls);
                if (call == 1) { started.Set(); return first.Task; }
                return Success(value);
            }};
            using (service = new OcrService(engine, delegate(Guid id, OcrResult result)
            {
                if (id == one) callbackEnqueued = service.TryEnqueue(three, "third");
                if (Interlocked.Increment(ref delivered) == 3) finished.Set();
            }, 2))
            {
                Assert(service.TryEnqueue(one, "first"), "First job should be accepted.");
                Wait(started, "First engine operation did not start.");
                Assert(!service.TryEnqueue(one, "duplicate"), "Active duplicate must be rejected.");
                Assert(service.TryEnqueue(two, "second"), "Second job should queue.");
                Assert(!service.TryEnqueue(two, "duplicate"), "Queued duplicate must be rejected.");
                Assert(!service.TryEnqueue(three, "third"), "Capacity must include active work.");
                Assert(calls == 1, "OCR calls must remain sequential.");
                first.SetResult(new OcrResult(OcrResultStatus.Completed, "first"));
                Wait(finished, "Completion callback could not enqueue a replacement job.");
                Assert(callbackEnqueued && calls == 3, "A completed slot must be free before its callback.");
            }
        }

        private static void ByteBudget()
        {
            ManualResetEventSlim started = new ManualResetEventSlim();
            TaskCompletionSource<OcrResult> first = new TaskCompletionSource<OcrResult>();
            FakeEngine engine = new FakeEngine { Handler = delegate(string value, CancellationToken token)
            { started.Set(); return first.Task; }};
            using (OcrService service = new OcrService(engine, delegate { }, 5, 6))
            {
                Assert(!service.TryEnqueue(Guid.Empty, "a"), "Empty identity must be rejected.");
                Assert(!service.TryEnqueue(Guid.NewGuid(), new string('a', ImagePayload.MaxBase64Chars + 1)), "Oversized payload must be rejected.");
                Assert(service.TryEnqueue(Guid.NewGuid(), "abc"), "Six-byte payload should fit.");
                Wait(started, "Byte-budget job did not start.");
                Assert(!service.TryEnqueue(Guid.NewGuid(), "d"), "UTF16 bytes must be counted, including active work.");
            }
            first.SetResult(new OcrResult(OcrResultStatus.NoText, null));
        }

        private static void CancelQueued()
        {
            ManualResetEventSlim started = new ManualResetEventSlim();
            ManualResetEventSlim finished = new ManualResetEventSlim();
            TaskCompletionSource<OcrResult> first = new TaskCompletionSource<OcrResult>();
            List<string> seen = new List<string>();
            int deliveries = 0;
            FakeEngine engine = new FakeEngine { Handler = delegate(string value, CancellationToken token)
            {
                lock (seen) seen.Add(value);
                if (value == "first") { started.Set(); return first.Task; }
                return Success(value);
            }};
            using (OcrService service = new OcrService(engine, delegate { if (Interlocked.Increment(ref deliveries) == 2) finished.Set(); }, 2))
            {
                service.TryEnqueue(Guid.NewGuid(), "first");
                Wait(started, "First job did not start.");
                Guid removed = Guid.NewGuid();
                Assert(service.TryEnqueue(removed, "deleted"), "Second job should queue.");
                service.Cancel(removed);
                Assert(service.TryEnqueue(Guid.NewGuid(), "replacement"), "Cancel must free its queue slot immediately.");
                first.SetResult(new OcrResult(OcrResultStatus.Completed, "first"));
                Wait(finished, "Replacement did not finish.");
                lock (seen) Assert(seen.Count == 2 && !seen.Contains("deleted"), "Deleted queued images must never reach OCR.");
            }
        }

        private static void CancelActiveAndReplace()
        {
            ManualResetEventSlim started = new ManualResetEventSlim();
            ManualResetEventSlim finished = new ManualResetEventSlim();
            TaskCompletionSource<OcrResult> first = new TaskCompletionSource<OcrResult>();
            CancellationToken activeToken = CancellationToken.None;
            int calls = 0, deliveries = 0;
            string actual = null;
            FakeEngine engine = new FakeEngine { Handler = delegate(string value, CancellationToken token)
            {
                if (Interlocked.Increment(ref calls) == 1) { activeToken = token; started.Set(); return first.Task; }
                return Success(value);
            }};
            using (OcrService service = new OcrService(engine, delegate(Guid id, OcrResult result)
            { actual = result.Text; Interlocked.Increment(ref deliveries); finished.Set(); }, 1))
            {
                Guid id = Guid.NewGuid();
                service.TryEnqueue(id, "old");
                Wait(started, "First job did not start.");
                service.Cancel(id);
                Assert(activeToken.IsCancellationRequested, "Active engine should receive cancellation.");
                Assert(service.TryEnqueue(id, "new"), "Cancelled identity should permit a replacement.");
                first.SetResult(new OcrResult(OcrResultStatus.Completed, "old"));
                Wait(finished, "Replacement did not complete.");
                Assert(actual == "new" && deliveries == 1 && calls == 2, "Stale result must not remove or deliver the replacement.");
            }
        }

        private static void Disposal()
        {
            ManualResetEventSlim started = new ManualResetEventSlim();
            TaskCompletionSource<OcrResult> first = new TaskCompletionSource<OcrResult>();
            CancellationToken activeToken = CancellationToken.None;
            int calls = 0, deliveries = 0;
            FakeEngine engine = new FakeEngine { Handler = delegate(string value, CancellationToken token)
            { Interlocked.Increment(ref calls); activeToken = token; started.Set(); return first.Task; }};
            OcrService service = new OcrService(engine, delegate { Interlocked.Increment(ref deliveries); });
            service.TryEnqueue(Guid.NewGuid(), "first");
            Wait(started, "First job did not start.");
            service.TryEnqueue(Guid.NewGuid(), "second");
            service.Dispose();
            service.Dispose();
            Assert(activeToken.IsCancellationRequested, "Disposal must cancel active work.");
            Assert(!service.TryEnqueue(Guid.NewGuid(), "third"), "Disposed service must reject all work.");
            first.SetResult(new OcrResult(OcrResultStatus.Completed, "late"));
            Thread.Sleep(100);
            Assert(calls == 1 && deliveries == 0, "Disposal must discard pending jobs and late results.");
        }

        private static void FailureAndStatus()
        {
            ManualResetEventSlim finished = new ManualResetEventSlim();
            List<OcrResultStatus> results = new List<OcrResultStatus>();
            FakeEngine engine = new FakeEngine { Handler = delegate(string value, CancellationToken token)
            {
                if (value == "throw") throw new InvalidOperationException("synthetic");
                if (value == "none") return Task.FromResult(new OcrResult(OcrResultStatus.NoText, null));
                if (value == "unavailable") return Task.FromResult(new OcrResult(OcrResultStatus.Unavailable, null));
                return Success("recognized");
            }};
            using (OcrService service = new OcrService(engine, delegate(Guid id, OcrResult result)
            { lock (results) { results.Add(result.Status); if (results.Count == 4) finished.Set(); } }))
            {
                foreach (string value in new string[] { "throw", "none", "unavailable", "success" })
                    Assert(service.TryEnqueue(Guid.NewGuid(), value), "All jobs should fit.");
                Wait(finished, "One failed job stopped the queue.");
                lock (results) Assert(results[0] == OcrResultStatus.Failed && results[1] == OcrResultStatus.NoText &&
                    results[2] == OcrResultStatus.Unavailable && results[3] == OcrResultStatus.Completed, "Statuses were lost.");
            }
        }

        private static void SynchronousCancellation()
        {
            ManualResetEventSlim started = new ManualResetEventSlim();
            TaskCompletionSource<OcrResult> pending = new TaskCompletionSource<OcrResult>();
            int deliveries = 0, calls = 0;
            FakeEngine engine = new FakeEngine { Handler = delegate(string value, CancellationToken token)
            {
                Interlocked.Increment(ref calls);
                token.Register(delegate { pending.TrySetCanceled(); });
                started.Set();
                return pending.Task;
            }};
            OcrService service = new OcrService(engine, delegate { Interlocked.Increment(ref deliveries); });
            service.TryEnqueue(Guid.NewGuid(), "active");
            Wait(started, "Active job did not start.");
            service.TryEnqueue(Guid.NewGuid(), "queued");
            // Let the worker attach its continuation to the incomplete Task.
            Thread.Sleep(50);
            service.Dispose();
            Assert(pending.Task.IsCanceled && calls == 1 && deliveries == 0,
                "Synchronous cancellation should finish active work without delivering or starting queued work.");
        }

        private static void CallbackFailure()
        {
            ManualResetEventSlim finished = new ManualResetEventSlim();
            int count = 0;
            FakeEngine engine = new FakeEngine { Handler = delegate(string value, CancellationToken token) { return Success(value); } };
            using (OcrService service = new OcrService(engine, delegate
            {
                if (Interlocked.Increment(ref count) == 1) throw new InvalidOperationException("synthetic dispatch failure");
                finished.Set();
            }))
            {
                service.TryEnqueue(Guid.NewGuid(), "first");
                service.TryEnqueue(Guid.NewGuid(), "second");
                Wait(finished, "Callback exception stopped the worker.");
            }
        }

        private static int NativeSmoke()
        {
            try
            {
                // A synthetic screenshot only; never reads or changes the real clipboard.
                using (Bitmap bitmap = new Bitmap(1200, 300))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                using (Font font = new Font("Arial", 42, FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    graphics.Clear(Color.White);
                    graphics.DrawString("DOLLY PASTE OCR 123\nSCREENSHOT TEXT", font, Brushes.Black, 35, 35);
                    OcrResult result = new WindowsOcrEngine().RecognizeAsync(ImagePayload.EncodePng(bitmap), CancellationToken.None).GetAwaiter().GetResult();
                    Console.WriteLine("Native Windows OCR status: " + result.Status);
                    Console.WriteLine(result.Text ?? "No extracted text.");
                    if (result.Status == OcrResultStatus.Unavailable)
                    {
                        Console.WriteLine("SKIP: Windows OCR is unavailable; packaged identity and installed OCR languages may be required.");
                        return 0;
                    }
                    Assert(result.Status == OcrResultStatus.Completed && result.Text.IndexOf("DOLLY", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        result.Text.IndexOf("SCREENSHOT", StringComparison.OrdinalIgnoreCase) >= 0 && result.Text.Contains("\n"),
                        "Native OCR did not recognize the synthetic screenshot and its line boundaries.");
                }
                using (Bitmap oversized = new Bitmap(6000, 400))
                using (Graphics graphics = Graphics.FromImage(oversized))
                using (Font font = new Font("Arial", 110, FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    graphics.Clear(Color.White);
                    graphics.DrawString("SCALED SCREENSHOT OCR 456", font, Brushes.Black, 60, 60);
                    OcrResult scaled = new WindowsOcrEngine().RecognizeAsync(ImagePayload.EncodePng(oversized), CancellationToken.None).GetAwaiter().GetResult();
                    Assert(scaled.Status == OcrResultStatus.Completed && scaled.Text.Contains("456"), "OCR scaling failed for a 6000-pixel screenshot.");
                    Console.WriteLine("Oversized screenshot: " + scaled.Text);
                    graphics.Clear(Color.White);
                    OcrResult blank = new WindowsOcrEngine().RecognizeAsync(ImagePayload.EncodePng(oversized), CancellationToken.None).GetAwaiter().GetResult();
                    Assert(blank.Status == OcrResultStatus.NoText, "A blank screenshot should report no text.");
                    Console.WriteLine("Blank screenshot: " + blank.Status);
                }
                OcrResult malformed = new WindowsOcrEngine().RecognizeAsync("not PNG", CancellationToken.None).GetAwaiter().GetResult();
                Assert(malformed.Status == OcrResultStatus.Failed, "Malformed input must be rejected.");
                return 0;
            }
            catch (Exception ex) { Console.WriteLine("Native OCR smoke failed: " + ex); return 1; }
        }
    }
}
