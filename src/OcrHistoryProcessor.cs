using System;
using System.Collections.Generic;

namespace DollyPaste
{
    // All history updates and queue bookkeeping run through the application's UI dispatcher.
    // The engine only receives immutable image values and never accesses the clipboard.
    internal sealed class OcrHistoryProcessor : IDisposable
    {
        private readonly HistoryStore _history;
        private readonly Action<Action> _dispatch;
        private readonly OcrService _service;
        private readonly HashSet<Guid> _scheduled = new HashSet<Guid>();
        private bool _scheduling;
        private bool _disposed;

        internal OcrHistoryProcessor(HistoryStore history, IOcrEngine engine, Action<Action> dispatch)
        {
            if (history == null) throw new ArgumentNullException("history");
            if (dispatch == null) throw new ArgumentNullException("dispatch");
            _history = history;
            _dispatch = dispatch;
            _service = new OcrService(engine, OnRecognized, 4);

            // Retry transient failures once on the next launch, including newly installed languages.
            foreach (ClipEntry entry in history.Snapshot())
                if (entry.Kind == "IMAGE" && !entry.IsSensitive &&
                    (entry.OcrStatus == OcrStates.Unavailable || entry.OcrStatus == OcrStates.Failed))
                    history.SetOcrResult(entry.Id, null, OcrStates.Pending);

            _history.Changed += OnHistoryChanged;
            SchedulePendingImages();
        }

        private void OnHistoryChanged(object sender, EventArgs args)
        {
            if (!_disposed) _dispatch(SchedulePendingImages);
        }

        private void SchedulePendingImages()
        {
            if (_disposed || _scheduling) return;
            _scheduling = true;
            try
            {
                List<ClipEntry> entries = _history.Snapshot();
                HashSet<Guid> pending = new HashSet<Guid>();
                foreach (ClipEntry entry in entries)
                    if (entry.Kind == "IMAGE" && !entry.IsSensitive && entry.OcrStatus == OcrStates.Pending)
                        pending.Add(entry.Id);

                foreach (Guid id in new List<Guid>(_scheduled))
                    if (!pending.Contains(id))
                    {
                        _service.Cancel(id);
                        _scheduled.Remove(id);
                    }

                foreach (ClipEntry entry in entries)
                    if (pending.Contains(entry.Id) && !_scheduled.Contains(entry.Id) &&
                        _service.TryEnqueue(entry.Id, entry.Text))
                        _scheduled.Add(entry.Id);
            }
            finally { _scheduling = false; }
        }

        private void OnRecognized(Guid id, OcrResult result)
        {
            if (_disposed) return;
            _dispatch(delegate
            {
                if (_disposed) return;
                _scheduled.Remove(id);
                string status;
                switch (result.Status)
                {
                    case OcrResultStatus.Completed: status = OcrStates.Complete; break;
                    case OcrResultStatus.NoText: status = OcrStates.NoText; break;
                    case OcrResultStatus.Unavailable: status = OcrStates.Unavailable; break;
                    default: status = OcrStates.Failed; break;
                }
                // The store rechecks expiry, deletion, and protection before accepting the result.
                _history.SetOcrResult(id, result.Text, status);
                SchedulePendingImages();
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _history.Changed -= OnHistoryChanged;
            _service.Dispose();
            _scheduled.Clear();
        }
    }
}
