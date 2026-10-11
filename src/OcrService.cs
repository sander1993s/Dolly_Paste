using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace DollyPaste
{
    internal enum OcrResultStatus { Completed, NoText, Unavailable, Failed }

    internal sealed class OcrResult
    {
        internal const int MaxTextLength = 32768;
        internal OcrResultStatus Status { get; private set; }
        internal string Text { get; private set; }

        internal OcrResult(OcrResultStatus status, string text)
        {
            Text = status == OcrResultStatus.Completed ? LimitText(text) : null;
            Status = status == OcrResultStatus.Completed && string.IsNullOrWhiteSpace(Text)
                ? OcrResultStatus.NoText : status;
            if (Status != OcrResultStatus.Completed) Text = null;
        }

        private static string LimitText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            text = text.Replace("\0", string.Empty).Trim();
            if (text.Length < MaxTextLength) return text;
            int length = Math.Min(MaxTextLength, text.Length);
            if (char.IsHighSurrogate(text[length - 1])) length--;
            return text.Substring(0, length).TrimEnd();
        }
    }

    internal interface IOcrEngine
    {
        Task<OcrResult> RecognizeAsync(string pngBase64, CancellationToken cancellationToken);
    }

    // Windows OCR is local. Microsoft supports the desktop API with package
    // identity; probe capability so unsupported installations fail gracefully.
    internal sealed class WindowsOcrEngine : IOcrEngine
    {
        public async Task<OcrResult> RecognizeAsync(string pngBase64, CancellationToken cancellationToken)
        {
            try
            {
                return await RecognizeWindowsAsync(pngBase64, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (TypeLoadException) { return new OcrResult(OcrResultStatus.Unavailable, null); }
            catch (FileNotFoundException) { return new OcrResult(OcrResultStatus.Unavailable, null); }
            catch (PlatformNotSupportedException) { return new OcrResult(OcrResultStatus.Unavailable, null); }
            catch (UnauthorizedAccessException) { return new OcrResult(OcrResultStatus.Unavailable, null); }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                // API/activation unavailable, access denied, or missing package identity.
                uint code = unchecked((uint)ex.HResult);
                bool unavailable = code == 0x80040154 || code == 0x80004001 ||
                    code == 0x80070005 || code == 0x80073D54;
                return new OcrResult(unavailable ? OcrResultStatus.Unavailable : OcrResultStatus.Failed, null);
            }
            catch (Exception) { return new OcrResult(OcrResultStatus.Failed, null); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static async Task<OcrResult> RecognizeWindowsAsync(string pngBase64, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(pngBase64) || pngBase64.Length > ImagePayload.MaxBase64Chars)
                return new OcrResult(OcrResultStatus.Failed, null);
            byte[] png = Convert.FromBase64String(pngBase64);
            if (!ImagePayload.HasValidPngHeader(png)) return new OcrResult(OcrResultStatus.Failed, null);

            OcrEngine engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null) return new OcrResult(OcrResultStatus.Unavailable, null);
            uint maximum = OcrEngine.MaxImageDimension;
            if (maximum == 0) return new OcrResult(OcrResultStatus.Unavailable, null);

            using (MemoryStream memory = new MemoryStream(png, false))
            using (IRandomAccessStream stream = memory.AsRandomAccessStream())
            {
                BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);
                if (decoder.PixelWidth > int.MaxValue || decoder.PixelHeight > int.MaxValue ||
                    !ImagePayload.AreDimensionsAllowed((int)decoder.PixelWidth, (int)decoder.PixelHeight))
                    return new OcrResult(OcrResultStatus.Failed, null);

                double scale = Math.Min(1.0, Math.Min((double)maximum / decoder.PixelWidth, (double)maximum / decoder.PixelHeight));
                BitmapTransform transform = new BitmapTransform();
                transform.ScaledWidth = Math.Max(1, (uint)Math.Floor(decoder.PixelWidth * scale));
                transform.ScaledHeight = Math.Max(1, (uint)Math.Floor(decoder.PixelHeight * scale));
                transform.InterpolationMode = BitmapInterpolationMode.Fant;
                using (SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied, transform, ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage).AsTask(cancellationToken).ConfigureAwait(false))
                {
                    Windows.Media.Ocr.OcrResult recognized = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    StringBuilder text = new StringBuilder();
                    foreach (OcrLine line in recognized.Lines)
                    {
                        if (text.Length > 0) text.Append(Environment.NewLine);
                        int remaining = OcrResult.MaxTextLength - text.Length;
                        if (remaining <= 0) break;
                        string lineText = line.Text ?? string.Empty;
                        text.Append(lineText, 0, Math.Min(lineText.Length, remaining));
                        if (text.Length >= OcrResult.MaxTextLength) break;
                    }
                    return new OcrResult(OcrResultStatus.Completed, text.ToString());
                }
            }
        }
    }

    // Owns a small, bounded queue; callbacks run on the worker thread. The caller
    // must marshal results and verify the image is still retained before saving.
    internal sealed class OcrService : IDisposable
    {
        private sealed class Job
        {
            internal Guid Id;
            internal string Payload;
            internal long ByteCount;
            internal CancellationTokenSource Cancellation = new CancellationTokenSource();
            internal LinkedListNode<Job> Node;
        }

        private readonly object _gate = new object();
        private readonly LinkedList<Job> _queue = new LinkedList<Job>();
        private readonly Dictionary<Guid, Job> _jobs = new Dictionary<Guid, Job>();
        private readonly IOcrEngine _engine;
        private readonly Action<Guid, OcrResult> _completed;
        private readonly int _maxPendingJobs;
        private readonly long _maxPendingBytes;
        private long _pendingBytes;
        private bool _running;
        private bool _disposed;

        internal OcrService(IOcrEngine engine, Action<Guid, OcrResult> completed,
            int maxPendingJobs = 4, long maxPendingBytes = 32L * 1024 * 1024)
        {
            if (engine == null) throw new ArgumentNullException("engine");
            if (completed == null) throw new ArgumentNullException("completed");
            if (maxPendingJobs < 1) throw new ArgumentOutOfRangeException("maxPendingJobs");
            if (maxPendingBytes < 1) throw new ArgumentOutOfRangeException("maxPendingBytes");
            _engine = engine;
            _completed = completed;
            _maxPendingJobs = maxPendingJobs;
            _maxPendingBytes = maxPendingBytes;
        }

        internal bool TryEnqueue(Guid id, string pngBase64)
        {
            if (id == Guid.Empty || string.IsNullOrEmpty(pngBase64) || pngBase64.Length > ImagePayload.MaxBase64Chars)
                return false;
            long byteCount = (long)pngBase64.Length * sizeof(char);
            lock (_gate)
            {
                if (_disposed || _jobs.ContainsKey(id) || _jobs.Count >= _maxPendingJobs ||
                    byteCount > _maxPendingBytes - _pendingBytes) return false;
                Job job = new Job { Id = id, Payload = pngBase64, ByteCount = byteCount };
                _jobs.Add(id, job);
                _pendingBytes += byteCount;
                job.Node = _queue.AddLast(job);
                if (!_running)
                {
                    _running = true;
                    Task.Run((Func<Task>)RunAsync);
                }
                return true;
            }
        }

        internal void Cancel(Guid id)
        {
            lock (_gate)
            {
                Job job;
                if (!_jobs.TryGetValue(id, out job)) return;
                RemoveJob(job);
                job.Cancellation.Cancel();
                job.Payload = null;
                if (job.Node != null)
                {
                    _queue.Remove(job.Node);
                    job.Node = null;
                    job.Cancellation.Dispose();
                }
            }
        }

        private async Task RunAsync()
        {
            while (true)
            {
                Job job;
                string payload;
                lock (_gate)
                {
                    if (_queue.Count == 0)
                    {
                        _running = false;
                        return;
                    }
                    job = _queue.First.Value;
                    _queue.RemoveFirst();
                    job.Node = null;
                    payload = job.Payload;
                    if (_disposed || job.Cancellation.IsCancellationRequested)
                    {
                        job.Cancellation.Dispose();
                        continue;
                    }
                }

                OcrResult result;
                try
                {
                    result = await _engine.RecognizeAsync(payload, job.Cancellation.Token).ConfigureAwait(false);
                    if (result == null) result = new OcrResult(OcrResultStatus.Failed, null);
                }
                catch (Exception) { result = new OcrResult(OcrResultStatus.Failed, null); }
                payload = null;
                lock (_gate)
                {
                    Job current;
                    bool deliver = !_disposed && !job.Cancellation.IsCancellationRequested &&
                        _jobs.TryGetValue(job.Id, out current) && object.ReferenceEquals(current, job);
                    RemoveJob(job);
                    job.Payload = null;
                    job.Cancellation.Dispose();
                    // Serialize delivery with disposal/cancellation. UI callers use
                    // BeginInvoke, so the worker never waits for the UI thread.
                    if (deliver)
                    {
                        try { _completed(job.Id, result); }
                        catch (Exception) { /* A failed UI dispatch must not stop later jobs. */ }
                    }
                }
            }
        }

        private void RemoveJob(Job job)
        {
            Job current;
            if (_jobs.TryGetValue(job.Id, out current) && object.ReferenceEquals(current, job))
            {
                _jobs.Remove(job.Id);
                _pendingBytes -= job.ByteCount;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                // Cancellation can complete an engine Task inline and reenter
                // RunAsync. Detach every job before invoking token callbacks.
                List<Job> jobs = new List<Job>(_jobs.Values);
                _jobs.Clear();
                _pendingBytes = 0;
                _queue.Clear();
                foreach (Job job in jobs)
                {
                    bool queued = job.Node != null;
                    job.Node = null;
                    job.Payload = null;
                    job.Cancellation.Cancel();
                    if (queued) job.Cancellation.Dispose();
                }
            }
        }
    }
}
