using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using DollyPaste;

namespace DollyPaste.Tests
{
    public static class OcrIntegrationTests
    {
        private sealed class Dispatcher
        {
            private readonly Queue<Action> _actions = new Queue<Action>();
            internal void Post(Action action) { lock (_actions) _actions.Enqueue(action); }
            internal void Drain()
            {
                while (true)
                {
                    Action action;
                    lock (_actions)
                    {
                        if (_actions.Count == 0) return;
                        action = _actions.Dequeue();
                    }
                    action();
                }
            }
            internal void Until(Func<bool> condition)
            {
                Stopwatch timer = Stopwatch.StartNew();
                do
                {
                    Drain();
                    if (condition()) return;
                    Thread.Sleep(5);
                } while (timer.Elapsed < TimeSpan.FromSeconds(10));
                throw new Exception("Timed out waiting for OCR/history dispatch.");
            }
        }

        private sealed class FakeEngine : IOcrEngine
        {
            internal Func<string, CancellationToken, Task<OcrResult>> Handler;
            public Task<OcrResult> RecognizeAsync(string payload, CancellationToken token)
            { return Handler(payload, token); }
        }

        private static int _failed;
        public static int Main()
        {
            Run("Backlog beyond four slots drains and preserves base64", Backlog);
            Run("Deleted in-flight image cannot overwrite a recapture", DeletedAndRecaptured);
            Run("Expired and protected images reject delayed results", ExpiredAndProtected);
            Run("Posted completion is ignored after processor disposal", Disposed);
            Run("Startup retries unavailable OCR without redoing completed images", StartupRetry);
            Console.WriteLine("OCR integration tests: " + (5 - _failed) + " passed, " + _failed + " failed.");
            return _failed == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            try { test(); Console.WriteLine("[PASS] " + name); }
            catch (Exception error) { _failed++; Console.WriteLine("[FAIL] " + name + ": " + error.Message); }
        }

        private static void Assert(bool condition, string message)
        { if (!condition) throw new Exception(message); }

        private static string Image(int seed)
        {
            using (Bitmap bitmap = new Bitmap(2, 2))
            {
                bitmap.SetPixel(0, 0, Color.FromArgb(255, seed, 20, 30));
                return ImagePayload.EncodePng(bitmap);
            }
        }

        private static OcrResult Result(string text)
        { return new OcrResult(OcrResultStatus.Completed, text); }

        private static void Backlog()
        {
            Dispatcher dispatcher = new Dispatcher();
            int calls = 0;
            FakeEngine engine = new FakeEngine { Handler = delegate(string payload, CancellationToken token)
            { Interlocked.Increment(ref calls); return Task.FromResult(Result("recognized " + payload)); } };
            using (HistoryStore store = new HistoryStore(new Settings(), null))
            {
                List<ClipEntry> captured = new List<ClipEntry>();
                for (int i = 1; i <= 9; i++) captured.Add(store.CaptureImage(Image(i), (uint)i));
                using (OcrHistoryProcessor processor = new OcrHistoryProcessor(store, engine, dispatcher.Post))
                {
                    dispatcher.Until(delegate
                    {
                        foreach (ClipEntry entry in store.Snapshot())
                            if (entry.OcrStatus != OcrStates.Complete) return false;
                        return true;
                    });
                    Assert(calls == 9, "Each image must be recognized exactly once across refill cycles.");
                    foreach (ClipEntry before in captured)
                    {
                        ClipEntry after = store.GetImageForOcr(before.Id);
                        Assert(after.Text == before.Text && after.CapturedUtc == before.CapturedUtc,
                            "OCR must preserve the original image and retention age.");
                        Assert(after.OcrText == "recognized " + before.Text, "Result must match its image identity.");
                    }
                }
            }
        }

        private static void DeletedAndRecaptured()
        {
            Dispatcher dispatcher = new Dispatcher();
            TaskCompletionSource<OcrResult> first = new TaskCompletionSource<OcrResult>();
            int calls = 0;
            FakeEngine engine = new FakeEngine { Handler = delegate(string payload, CancellationToken token)
            { return Interlocked.Increment(ref calls) == 1 ? first.Task : Task.FromResult(Result("new text")); } };
            using (HistoryStore store = new HistoryStore(new Settings(), null))
            {
                string payload = Image(11);
                ClipEntry old = store.CaptureImage(payload, 11);
                using (OcrHistoryProcessor processor = new OcrHistoryProcessor(store, engine, dispatcher.Post))
                {
                    dispatcher.Until(delegate { return calls == 1; });
                    store.Remove(old.Id);
                    dispatcher.Drain();
                    ClipEntry replacement = store.CaptureImage(payload, 12);
                    dispatcher.Drain();
                    first.SetResult(Result("stale text"));
                    dispatcher.Until(delegate { return store.GetOcrText(replacement.Id) == "new text"; });
                    Assert(old.Id != replacement.Id && store.GetImageForOcr(old.Id) == null,
                        "Deleted identity must never be restored.");
                    Assert(store.Snapshot().Count == 1, "Only the newly captured image should remain.");
                }
            }
        }

        private static void ExpiredAndProtected()
        {
            foreach (bool protect in new[] { false, true })
            {
                Dispatcher dispatcher = new Dispatcher();
                DateTime now = DateTime.UtcNow;
                TaskCompletionSource<OcrResult> task = new TaskCompletionSource<OcrResult>();
                ManualResetEventSlim posted = new ManualResetEventSlim();
                FakeEngine engine = new FakeEngine { Handler = delegate(string payload, CancellationToken token)
                { return task.Task; } };
                using (posted)
                using (HistoryStore store = new HistoryStore(new Settings { MaxAgeMinutes = 1 }, null, delegate { return now; }))
                {
                    ClipEntry entry = store.CaptureImage(Image(21), 21);
                    using (OcrHistoryProcessor processor = new OcrHistoryProcessor(store, engine, delegate(Action action)
                    { dispatcher.Post(action); posted.Set(); }))
                    {
                        task.SetResult(Result("must not survive"));
                        Assert(posted.Wait(TimeSpan.FromSeconds(10)), "Expected a queued OCR completion.");
                        if (protect) store.Protect(21); else now = now.AddMinutes(2);
                        // Completion has definitely been posted. Its store write must recheck
                        // expiry/protection, even though the worker has already finished.
                        dispatcher.Drain();
                        Assert(store.GetImageForOcr(entry.Id) == null && store.GetOcrText(entry.Id) == null,
                            "Expired or protected images must not expose a delayed OCR result.");
                    }
                    dispatcher.Drain();
                }
            }
        }

        private static void Disposed()
        {
            Dispatcher dispatcher = new Dispatcher();
            ManualResetEventSlim posted = new ManualResetEventSlim();
            FakeEngine engine = new FakeEngine { Handler = delegate { return Task.FromResult(Result("late text")); } };
            using (HistoryStore store = new HistoryStore(new Settings(), null))
            {
                ClipEntry entry = store.CaptureImage(Image(31), 31);
                OcrHistoryProcessor processor = new OcrHistoryProcessor(store, engine, delegate(Action action)
                { dispatcher.Post(action); posted.Set(); });
                try
                {
                    Assert(posted.Wait(TimeSpan.FromSeconds(10)), "Expected a queued completion.");
                    processor.Dispose();
                    dispatcher.Drain();
                    Assert(store.GetOcrText(entry.Id) == null, "Disposal must invalidate already-posted completion.");
                }
                finally { processor.Dispose(); posted.Dispose(); }
            }
        }

        private static void StartupRetry()
        {
            Dispatcher dispatcher = new Dispatcher();
            int calls = 0;
            FakeEngine engine = new FakeEngine { Handler = delegate
            { Interlocked.Increment(ref calls); return Task.FromResult(Result("retry text")); } };
            using (HistoryStore store = new HistoryStore(new Settings(), null))
            {
                ClipEntry retry = store.CaptureImage(Image(41), 41);
                ClipEntry done = store.CaptureImage(Image(42), 42);
                store.SetOcrResult(retry.Id, null, OcrStates.Unavailable);
                store.SetOcrResult(done.Id, "retained text", OcrStates.Complete);
                using (OcrHistoryProcessor processor = new OcrHistoryProcessor(store, engine, dispatcher.Post))
                {
                    dispatcher.Until(delegate { return store.GetOcrText(retry.Id) == "retry text"; });
                    Assert(calls == 1 && store.GetOcrText(done.Id) == "retained text",
                        "Startup should retry unavailable images and preserve completed text.");
                }
            }
        }
    }
}
