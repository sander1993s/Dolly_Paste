using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace DollyPaste
{
    public enum SensitiveBehavior
    {
        Censor,
        Discard
    }

    [DataContract]
    public sealed class Settings
    {
        [DataMember(Name = "maxAgeMinutes")]
        public int MaxAgeMinutes { get; set; }

        [DataMember(Name = "maxItems")]
        public int MaxItems { get; set; }

        [DataMember(Name = "persistHistory")]
        public bool PersistHistory { get; set; }

        [DataMember(Name = "sensitiveBehavior")]
        public SensitiveBehavior SensitiveBehavior { get; set; }

        [DataMember(Name = "protectUnknownTargets")]
        public bool ProtectUnknownTargets { get; set; }

        public Settings()
        {
            SetDefaults(default(StreamingContext));
        }

        [OnDeserializing]
        private void SetDefaults(StreamingContext context)
        {
            MaxAgeMinutes = 1440;
            MaxItems = 200;
            PersistHistory = false;
            SensitiveBehavior = SensitiveBehavior.Censor;
            ProtectUnknownTargets = true;
        }

        public Settings Clone()
        {
            Settings clone = new Settings();
            clone.MaxAgeMinutes = this.MaxAgeMinutes;
            clone.MaxItems = this.MaxItems;
            clone.PersistHistory = this.PersistHistory;
            clone.SensitiveBehavior = this.SensitiveBehavior;
            clone.ProtectUnknownTargets = this.ProtectUnknownTargets;
            return clone;
        }

        public void Normalize()
        {
            if (MaxAgeMinutes < 1)
            {
                MaxAgeMinutes = 1;
            }
            else if (MaxAgeMinutes > 43200)
            {
                MaxAgeMinutes = 43200;
            }

            if (MaxItems < 10)
            {
                MaxItems = 10;
            }
            else if (MaxItems > 1000)
            {
                MaxItems = 1000;
            }

            if (SensitiveBehavior != SensitiveBehavior.Censor && SensitiveBehavior != SensitiveBehavior.Discard)
            {
                SensitiveBehavior = SensitiveBehavior.Censor;
            }
        }
    }

    public static class SettingsStore
    {
        public static Settings Load(string dataDirectory)
        {
            if (string.IsNullOrEmpty(dataDirectory))
            {
                Settings def = new Settings();
                def.Normalize();
                return def;
            }

            string filePath = Path.Combine(dataDirectory, "settings.json");
            if (!File.Exists(filePath))
            {
                Settings def = new Settings();
                def.Normalize();
                return def;
            }

            try
            {
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(Settings));
                    Settings loaded = (Settings)serializer.ReadObject(fs);
                    if (loaded == null)
                    {
                        loaded = new Settings();
                    }
                    loaded.Normalize();
                    return loaded;
                }
            }
            catch
            {
                Settings def = new Settings();
                def.Normalize();
                return def;
            }
        }

        public static void Save(string dataDirectory, Settings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }
            if (string.IsNullOrEmpty(dataDirectory))
            {
                throw new ArgumentException("dataDirectory cannot be null or empty", "dataDirectory");
            }

            Directory.CreateDirectory(dataDirectory);
            string filePath = Path.Combine(dataDirectory, "settings.json");
            string tmpPath = filePath + ".tmp";

            Settings toSave = settings.Clone();
            toSave.Normalize();

            try
            {
                using (FileStream fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(Settings));
                    serializer.WriteObject(fs, toSave);
                    fs.Flush(true);
                }

                if (File.Exists(filePath))
                {
                    File.Replace(tmpPath, filePath, null);
                }
                else
                {
                    File.Move(tmpPath, filePath);
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(tmpPath))
                    {
                        File.Delete(tmpPath);
                    }
                }
                catch
                {
                }
            }
        }
    }

    public sealed class ClipEntry
    {
        public Guid Id { get; private set; }
        public string Text { get; private set; }
        public DateTime CapturedUtc { get; private set; }
        public bool IsPinned { get; internal set; }
        public bool IsSensitive { get; internal set; }
        public string Kind { get; private set; }

        public ClipEntry(Guid id, string text, DateTime capturedUtc, bool isPinned, bool isSensitive, string kind)
        {
            Id = id;
            CapturedUtc = capturedUtc;
            IsPinned = isPinned;
            IsSensitive = isSensitive;
            Kind = string.IsNullOrEmpty(kind) ? "TEXT" : kind;
            Text = isSensitive ? string.Empty : (text ?? string.Empty);
        }

        internal ClipEntry Clone()
        {
            return new ClipEntry(Id, Text, CapturedUtc, IsPinned, IsSensitive, Kind);
        }

        internal void ConvertToTombstone()
        {
            Text = string.Empty;
            IsSensitive = true;
        }
    }

    [DataContract]
    internal sealed class HistoryRecordDto
    {
        [DataMember(Name = "id")]
        public Guid Id { get; set; }

        [DataMember(Name = "text")]
        public string Text { get; set; }

        [DataMember(Name = "capturedUtc")]
        public DateTime CapturedUtc { get; set; }

        [DataMember(Name = "isPinned")]
        public bool IsPinned { get; set; }

        [DataMember(Name = "isSensitive")]
        public bool IsSensitive { get; set; }

        [DataMember(Name = "kind")]
        public string Kind { get; set; }
    }

    [DataContract]
    internal sealed class HistoryFileDto
    {
        [DataMember(Name = "version")]
        public int Version { get; set; }

        [DataMember(Name = "records")]
        public List<HistoryRecordDto> Records { get; set; }
    }

    public sealed class HistoryStore : IDisposable
    {
        private readonly object _lock = new object();
        private readonly string _dataDirectory;
        private readonly Func<DateTime> _utcClock;
        private readonly byte[] _sessionKey;
        private readonly List<ClipEntry> _entries;
        private readonly HashSet<string> _sensitiveFingerprints;
        private readonly Dictionary<uint, string> _sequenceToFingerprint;
        private readonly Queue<uint> _sequenceOrder;
        private readonly HashSet<uint> _pendingProtectedSequences;
        private readonly Queue<uint> _pendingOrder;

        private const int MaxTrackedSequences = 256;
        private const int MaxSuppressionCapacity = 1024;
        private const int MaxPayloadBytes = 4 * 1024 * 1024; // 4 MiB
        private const int MaxTextChars = 32768;

        private Settings _settings;
        private string _storageWarning;
        private bool _captureSuspended;
        private uint _maxObservedSequence;
        private bool _isFiringChanged;
        private bool _hasPendingChange;
        private bool _disposed;

        public event EventHandler Changed;

        public Settings Settings
        {
            get
            {
                lock (_lock)
                {
                    return _settings.Clone();
                }
            }
        }

        public string StorageWarning
        {
            get
            {
                lock (_lock)
                {
                    return _storageWarning;
                }
            }
        }

        public HistoryStore(Settings settings, string dataDirectory)
            : this(settings, dataDirectory, null)
        {
        }

        public HistoryStore(Settings settings, string dataDirectory, Func<DateTime> utcClock)
        {
            if (settings == null)
            {
                settings = new Settings();
            }
            _settings = settings.Clone();
            _settings.Normalize();

            _dataDirectory = dataDirectory;
            _utcClock = utcClock != null ? utcClock : (Func<DateTime>)delegate() { return DateTime.UtcNow; };

            _sessionKey = new byte[32];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(_sessionKey);
            }

            _entries = new List<ClipEntry>();
            _sensitiveFingerprints = new HashSet<string>(StringComparer.Ordinal);
            _sequenceToFingerprint = new Dictionary<uint, string>();
            _sequenceOrder = new Queue<uint>();
            _pendingProtectedSequences = new HashSet<uint>();
            _pendingOrder = new Queue<uint>();

            if (_settings.PersistHistory && !string.IsNullOrEmpty(_dataDirectory))
            {
                LoadPersistedHistory();
            }
            else if (!string.IsNullOrEmpty(_dataDirectory))
            {
                CleanupPersistedFilesLocked();
            }
        }

        public List<ClipEntry> Snapshot()
        {
            List<ClipEntry> copy = new List<ClipEntry>();
            bool stateChanged = false;
            lock (_lock)
            {
                stateChanged = PruneLocked();
                if (stateChanged && _settings.PersistHistory)
                {
                    PersistLocked();
                }
                for (int i = 0; i < _entries.Count; i++)
                {
                    copy.Add(_entries[i].Clone());
                }
            }
            if (stateChanged)
            {
                TriggerChanged();
            }
            return copy;
        }

        public ClipEntry Capture(string text, uint sequence)
        {
            if (string.IsNullOrEmpty(text) || text.Length > MaxTextChars)
            {
                return null;
            }

            ClipEntry result = null;
            bool stateChanged = false;

            lock (_lock)
            {
                if (sequence > _maxObservedSequence)
                {
                    _maxObservedSequence = sequence;
                }

                string fingerprint = ComputeFingerprint(text);

                bool isSensitive = _pendingProtectedSequences.Contains(sequence) || _sensitiveFingerprints.Contains(fingerprint);

                if (isSensitive)
                {
                    _pendingProtectedSequences.Remove(sequence);
                    stateChanged = ScrubFingerprint(fingerprint);

                    if (_settings.SensitiveBehavior == SensitiveBehavior.Discard)
                    {
                        if (stateChanged && _settings.PersistHistory)
                        {
                            PersistLocked();
                        }
                        result = null;
                    }
                    else
                    {
                        if (!_captureSuspended)
                        {
                            DateTime now = _utcClock();
                            ClipEntry tombstone = new ClipEntry(
                                Guid.NewGuid(),
                                string.Empty,
                                now,
                                false,
                                true,
                                DetermineKind(text)
                            );
                            _entries.Insert(0, tombstone);
                            stateChanged = true;

                            PruneLocked();
                            if (_settings.PersistHistory)
                            {
                                PersistLocked();
                            }
                            result = tombstone.Clone();
                        }
                        else
                        {
                            if (stateChanged && _settings.PersistHistory)
                            {
                                PersistLocked();
                            }
                            result = null;
                        }
                    }
                }
                else
                {
                    RecordSequenceFingerprint(sequence, fingerprint);

                    if (!_captureSuspended)
                    {
                        DateTime now = _utcClock();

                        int existingIndex = -1;
                        for (int i = 0; i < _entries.Count; i++)
                        {
                            if (!_entries[i].IsSensitive && string.Equals(_entries[i].Text, text, StringComparison.Ordinal))
                            {
                                existingIndex = i;
                                break;
                            }
                        }

                        if (existingIndex >= 0)
                        {
                            // Recopy/dedup: move to top, preserve original CapturedUtc
                            ClipEntry existing = _entries[existingIndex];
                            _entries.RemoveAt(existingIndex);
                            _entries.Insert(0, existing);
                            stateChanged = true;
                            result = existing.Clone();
                        }
                        else
                        {
                            ClipEntry newEntry = new ClipEntry(
                                Guid.NewGuid(),
                                text,
                                now,
                                false,
                                false,
                                DetermineKind(text)
                            );
                            _entries.Insert(0, newEntry);
                            stateChanged = true;
                            result = newEntry.Clone();
                        }

                        PruneLocked();
                        if (_settings.PersistHistory)
                        {
                            PersistLocked();
                        }
                    }
                }
            }

            if (stateChanged)
            {
                TriggerChanged();
            }

            return result;
        }

        public void TrackRestored(Guid id, uint sequence)
        {
            bool stateChanged = false;
            lock (_lock)
            {
                if (sequence > _maxObservedSequence)
                {
                    _maxObservedSequence = sequence;
                }

                ClipEntry entry = null;
                for (int i = 0; i < _entries.Count; i++)
                {
                    if (_entries[i].Id == id)
                    {
                        entry = _entries[i];
                        break;
                    }
                }

                if (entry == null || entry.IsSensitive || string.IsNullOrEmpty(entry.Text))
                {
                    return;
                }

                string fingerprint = ComputeFingerprint(entry.Text);

                if (_pendingProtectedSequences.Contains(sequence))
                {
                    _pendingProtectedSequences.Remove(sequence);
                    stateChanged = ScrubFingerprint(fingerprint);
                }
                else
                {
                    RecordSequenceFingerprint(sequence, fingerprint);
                }

                if (stateChanged && _settings.PersistHistory)
                {
                    PersistLocked();
                }
            }

            if (stateChanged)
            {
                TriggerChanged();
            }
        }

        public void Protect(uint sequence)
        {
            bool stateChanged = false;
            lock (_lock)
            {
                if (sequence > _maxObservedSequence)
                {
                    _maxObservedSequence = sequence;
                }

                string fingerprint;
                if (_sequenceToFingerprint.TryGetValue(sequence, out fingerprint))
                {
                    _sequenceToFingerprint.Remove(sequence);
                    stateChanged = ScrubFingerprint(fingerprint);
                }
                else
                {
                    if (_maxObservedSequence > MaxTrackedSequences && sequence < (_maxObservedSequence - MaxTrackedSequences))
                    {
                        // Old delayed sequence: ignore to prevent censoring unrelated new content
                    }
                    else
                    {
                        if (!_pendingProtectedSequences.Contains(sequence))
                        {
                            while (_pendingOrder.Count >= MaxTrackedSequences)
                            {
                                uint oldSeq = _pendingOrder.Dequeue();
                                _pendingProtectedSequences.Remove(oldSeq);
                            }
                            _pendingOrder.Enqueue(sequence);
                            _pendingProtectedSequences.Add(sequence);
                        }
                    }
                }

                if (stateChanged && _settings.PersistHistory)
                {
                    PersistLocked();
                }
            }

            if (stateChanged)
            {
                TriggerChanged();
            }
        }

        public void Remove(Guid id)
        {
            bool stateChanged = false;
            lock (_lock)
            {
                for (int i = 0; i < _entries.Count; i++)
                {
                    if (_entries[i].Id == id)
                    {
                        _entries.RemoveAt(i);
                        stateChanged = true;
                        break;
                    }
                }
                if (stateChanged && _settings.PersistHistory)
                {
                    PersistLocked();
                }
            }
            if (stateChanged)
            {
                TriggerChanged();
            }
        }

        public void TogglePin(Guid id)
        {
            bool stateChanged = false;
            lock (_lock)
            {
                for (int i = 0; i < _entries.Count; i++)
                {
                    if (_entries[i].Id == id)
                    {
                        _entries[i].IsPinned = !_entries[i].IsPinned;
                        stateChanged = true;
                        break;
                    }
                }
                if (stateChanged && _settings.PersistHistory)
                {
                    PersistLocked();
                }
            }
            if (stateChanged)
            {
                TriggerChanged();
            }
        }

        public void Clear()
        {
            bool stateChanged = false;
            lock (_lock)
            {
                if (_entries.Count > 0)
                {
                    _entries.Clear();
                    stateChanged = true;
                    if (_settings.PersistHistory)
                    {
                        PersistLocked();
                    }
                }
            }
            if (stateChanged)
            {
                TriggerChanged();
            }
        }

        public void UpdateSettings(Settings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            bool stateChanged = false;
            lock (_lock)
            {
                Settings newSettings = settings.Clone();
                newSettings.Normalize();

                _settings = newSettings;

                if (!_settings.PersistHistory && !string.IsNullOrEmpty(_dataDirectory))
                {
                    CleanupPersistedFilesLocked();
                }

                stateChanged = PruneLocked();

                if (_settings.PersistHistory)
                {
                    PersistLocked();
                }
            }

            if (stateChanged)
            {
                TriggerChanged();
            }
        }

        public void Prune()
        {
            bool stateChanged = false;
            lock (_lock)
            {
                stateChanged = PruneLocked();
                if (stateChanged && _settings.PersistHistory)
                {
                    PersistLocked();
                }
            }
            if (stateChanged)
            {
                TriggerChanged();
            }
        }

        public string GetText(Guid id)
        {
            bool stateChanged = false;
            string text = null;
            lock (_lock)
            {
                stateChanged = PruneLocked();
                if (stateChanged && _settings.PersistHistory)
                {
                    PersistLocked();
                }

                DateTime now = _utcClock();
                TimeSpan maxAge = TimeSpan.FromMinutes(_settings.MaxAgeMinutes);

                for (int i = 0; i < _entries.Count; i++)
                {
                    ClipEntry entry = _entries[i];
                    if (entry.Id == id)
                    {
                        if (entry.IsSensitive)
                        {
                            text = null;
                        }
                        else if ((now - entry.CapturedUtc) >= maxAge)
                        {
                            text = null;
                        }
                        else
                        {
                            text = entry.Text;
                        }
                        break;
                    }
                }
            }

            if (stateChanged)
            {
                TriggerChanged();
            }

            return text;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;

                _entries.Clear();
                _sensitiveFingerprints.Clear();
                _sequenceToFingerprint.Clear();
                _sequenceOrder.Clear();
                _pendingProtectedSequences.Clear();
                _pendingOrder.Clear();
                Array.Clear(_sessionKey, 0, _sessionKey.Length);
            }
        }

        private void CleanupPersistedFilesLocked()
        {
            if (string.IsNullOrEmpty(_dataDirectory))
            {
                return;
            }

            try
            {
                string filePath = Path.Combine(_dataDirectory, "history.dat");
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                string tmpPath = filePath + ".tmp";
                if (File.Exists(tmpPath))
                {
                    File.Delete(tmpPath);
                }

                if (File.Exists(filePath) || File.Exists(tmpPath))
                {
                    _storageWarning = "Failed to delete persisted history: files could not be removed.";
                }
                else
                {
                    if (_storageWarning != null && _storageWarning.StartsWith("Failed to delete persisted history", StringComparison.Ordinal))
                    {
                        _storageWarning = null;
                    }
                }
            }
            catch (Exception ex)
            {
                _storageWarning = "Failed to delete persisted history: " + ex.Message;
            }
        }

        private string ComputeFingerprint(string text)
        {
            if (text == null)
            {
                text = string.Empty;
            }
            byte[] textBytes = Encoding.UTF8.GetBytes(text);
            using (HMACSHA256 hmac = new HMACSHA256(_sessionKey))
            {
                byte[] hash = hmac.ComputeHash(textBytes);
                return BitConverter.ToString(hash).Replace("-", string.Empty);
            }
        }

        private void RecordSequenceFingerprint(uint sequence, string fingerprint)
        {
            if (!_sequenceToFingerprint.ContainsKey(sequence))
            {
                while (_sequenceOrder.Count >= MaxTrackedSequences)
                {
                    uint oldSeq = _sequenceOrder.Dequeue();
                    _sequenceToFingerprint.Remove(oldSeq);
                }
                _sequenceOrder.Enqueue(sequence);
            }
            _sequenceToFingerprint[sequence] = fingerprint;
        }

        private bool ScrubFingerprint(string fingerprint)
        {
            if (string.IsNullOrEmpty(fingerprint))
            {
                return false;
            }

            if (!_sensitiveFingerprints.Contains(fingerprint))
            {
                if (_sensitiveFingerprints.Count >= MaxSuppressionCapacity)
                {
                    _captureSuspended = true;
                    _storageWarning = "Suppression capacity reached; clipboard capture suspended.";
                }
                else
                {
                    _sensitiveFingerprints.Add(fingerprint);
                }
            }

            bool changed = false;

            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                ClipEntry entry = _entries[i];
                if (!entry.IsSensitive && !string.IsNullOrEmpty(entry.Text))
                {
                    if (string.Equals(ComputeFingerprint(entry.Text), fingerprint, StringComparison.Ordinal))
                    {
                        if (_settings.SensitiveBehavior == SensitiveBehavior.Discard)
                        {
                            _entries.RemoveAt(i);
                            changed = true;
                        }
                        else
                        {
                            entry.ConvertToTombstone();
                            changed = true;
                        }
                    }
                }
            }

            if (_settings.SensitiveBehavior == SensitiveBehavior.Discard)
            {
                for (int i = _entries.Count - 1; i >= 0; i--)
                {
                    if (_entries[i].IsSensitive)
                    {
                        _entries.RemoveAt(i);
                        changed = true;
                    }
                }
            }

            return changed;
        }

        private bool PruneLocked()
        {
            bool changed = false;
            DateTime now = _utcClock();
            TimeSpan maxAge = TimeSpan.FromMinutes(_settings.MaxAgeMinutes);

            // 1. Expiry boundary: age >= MaxAgeMinutes expires, including pins
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                TimeSpan age = now - _entries[i].CapturedUtc;
                if (age >= maxAge)
                {
                    _entries.RemoveAt(i);
                    changed = true;
                }
            }

            // 2. Discard sensitive tombstones if Discard mode
            if (_settings.SensitiveBehavior == SensitiveBehavior.Discard)
            {
                for (int i = _entries.Count - 1; i >= 0; i--)
                {
                    if (_entries[i].IsSensitive)
                    {
                        _entries.RemoveAt(i);
                        changed = true;
                    }
                }
            }

            // 3. Bound configured count (pins never bypass count)
            if (_entries.Count > _settings.MaxItems)
            {
                // First pass: remove oldest unpinned
                for (int i = _entries.Count - 1; i >= 0 && _entries.Count > _settings.MaxItems; i--)
                {
                    if (!_entries[i].IsPinned)
                    {
                        _entries.RemoveAt(i);
                        changed = true;
                    }
                }

                // Second pass: if still over MaxItems, remove oldest pinned
                while (_entries.Count > _settings.MaxItems)
                {
                    _entries.RemoveAt(_entries.Count - 1);
                    changed = true;
                }
            }

            // 4. Bound total retained payload to 4 MiB (pins never bypass size)
            int currentBytes = GetTotalPayloadBytesLocked();
            if (currentBytes > MaxPayloadBytes)
            {
                // First pass: remove oldest unpinned
                for (int i = _entries.Count - 1; i >= 0 && currentBytes > MaxPayloadBytes; i--)
                {
                    if (!_entries[i].IsPinned)
                    {
                        currentBytes -= Encoding.UTF8.GetByteCount(_entries[i].Text);
                        _entries.RemoveAt(i);
                        changed = true;
                    }
                }

                // Second pass: if still over 4 MiB, remove oldest pinned
                while (_entries.Count > 0 && currentBytes > MaxPayloadBytes)
                {
                    int lastIdx = _entries.Count - 1;
                    currentBytes -= Encoding.UTF8.GetByteCount(_entries[lastIdx].Text);
                    _entries.RemoveAt(lastIdx);
                    changed = true;
                }
            }

            return changed;
        }

        private int GetTotalPayloadBytesLocked()
        {
            int total = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (!_entries[i].IsSensitive && !string.IsNullOrEmpty(_entries[i].Text))
                {
                    total += Encoding.UTF8.GetByteCount(_entries[i].Text);
                }
            }
            return total;
        }

        private void TriggerChanged()
        {
            bool notify = false;
            lock (_lock)
            {
                if (_isFiringChanged)
                {
                    _hasPendingChange = true;
                }
                else
                {
                    _isFiringChanged = true;
                    notify = true;
                }
            }

            if (!notify) return;

            int iterations = 0;
            try
            {
                while (iterations < 25)
                {
                    iterations++;
                    EventHandler handler = Changed;
                    if (handler != null)
                    {
                        handler(this, EventArgs.Empty);
                    }

                    lock (_lock)
                    {
                        if (!_hasPendingChange)
                        {
                            _isFiringChanged = false;
                            break;
                        }
                        _hasPendingChange = false;
                    }
                }
            }
            finally
            {
                lock (_lock)
                {
                    _isFiringChanged = false;
                    _hasPendingChange = false;
                }
            }
        }

        private void LoadPersistedHistory()
        {
            if (string.IsNullOrEmpty(_dataDirectory)) return;

            string filePath = Path.Combine(_dataDirectory, "history.dat");
            if (!File.Exists(filePath)) return;

            try
            {
                FileInfo fi = new FileInfo(filePath);
                if (fi.Length > 8 * 1024 * 1024)
                {
                    _storageWarning = "Encrypted history file exceeds 8 MiB limit.";
                    return;
                }

                byte[] encryptedBytes;
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    encryptedBytes = new byte[(int)fi.Length];
                    int read = 0;
                    while (read < encryptedBytes.Length)
                    {
                        int r = fs.Read(encryptedBytes, read, encryptedBytes.Length - read);
                        if (r == 0) break;
                        read += r;
                    }
                }

                byte[] plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);

                List<ClipEntry> loaded = new List<ClipEntry>();
                using (MemoryStream ms = new MemoryStream(plainBytes))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(HistoryFileDto));
                    HistoryFileDto dto = (HistoryFileDto)serializer.ReadObject(ms);
                    if (dto != null && dto.Records != null)
                    {
                        DateTime now = _utcClock();
                        for (int i = 0; i < dto.Records.Count; i++)
                        {
                            HistoryRecordDto r = dto.Records[i];
                            if (r == null) continue;
                            if (r.Id == Guid.Empty) continue;
                            // Reject non-UTC, future or invalid timestamps
                            if (r.CapturedUtc.Kind != DateTimeKind.Utc) continue;
                            if (r.CapturedUtc > now || r.CapturedUtc <= DateTime.MinValue) continue;

                            string text = r.Text;
                            if (r.IsSensitive)
                            {
                                text = string.Empty;
                            }
                            else if (text == null || text.Length > MaxTextChars)
                            {
                                continue;
                            }

                            string kind = r.Kind;
                            if (kind != "TEXT" && kind != "LINK" && kind != "CODE")
                            {
                                kind = DetermineKind(text);
                            }

                            loaded.Add(new ClipEntry(r.Id, text, r.CapturedUtc, r.IsPinned, r.IsSensitive, kind));
                        }
                    }
                }

                _entries.Clear();
                _entries.AddRange(loaded);

                PruneLocked();
                PersistLocked();
            }
            catch (Exception ex)
            {
                _storageWarning = "Failed to load persisted history: " + ex.Message;
                _entries.Clear();
            }
        }

        private void PersistLocked()
        {
            if (!_settings.PersistHistory || string.IsNullOrEmpty(_dataDirectory))
            {
                return;
            }

            string filePath = Path.Combine(_dataDirectory, "history.dat");
            string tmpPath = filePath + ".tmp";

            try
            {
                Directory.CreateDirectory(_dataDirectory);

                HistoryFileDto dto = new HistoryFileDto();
                dto.Version = 1;
                dto.Records = new List<HistoryRecordDto>();

                for (int i = 0; i < _entries.Count; i++)
                {
                    ClipEntry e = _entries[i];
                    HistoryRecordDto r = new HistoryRecordDto();
                    r.Id = e.Id;
                    r.Text = e.IsSensitive ? string.Empty : e.Text;
                    r.CapturedUtc = DateTime.SpecifyKind(e.CapturedUtc, DateTimeKind.Utc);
                    r.IsPinned = e.IsPinned;
                    r.IsSensitive = e.IsSensitive;
                    r.Kind = e.Kind;
                    dto.Records.Add(r);
                }

                byte[] plainBytes;
                using (MemoryStream ms = new MemoryStream())
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(HistoryFileDto));
                    serializer.WriteObject(ms, dto);
                    plainBytes = ms.ToArray();
                }

                byte[] encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);

                using (FileStream fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(encryptedBytes, 0, encryptedBytes.Length);
                    fs.Flush(true);
                }

                if (File.Exists(filePath))
                {
                    File.Replace(tmpPath, filePath, null);
                }
                else
                {
                    File.Move(tmpPath, filePath);
                }
            }
            catch (Exception ex)
            {
                _storageWarning = "Failed to persist history: " + ex.Message;
            }
            finally
            {
                try
                {
                    if (File.Exists(tmpPath))
                    {
                        File.Delete(tmpPath);
                    }
                }
                catch (Exception ex)
                {
                    if (string.IsNullOrEmpty(_storageWarning))
                    {
                        _storageWarning = "Failed to clean up temporary history file: " + ex.Message;
                    }
                    else
                    {
                        _storageWarning += "; Failed to clean up temporary history file: " + ex.Message;
                    }
                }
            }
        }

        private static string DetermineKind(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "TEXT";
            }

            string trimmed = text.Trim();

            if (trimmed.IndexOf('\n') < 0 && trimmed.IndexOf('\r') < 0)
            {
                if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
                {
                    Uri uri;
                    if (Uri.TryCreate(trimmed, UriKind.Absolute, out uri) &&
                        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeFtp))
                    {
                        return "LINK";
                    }
                }
            }

            if (IsCodeLike(trimmed))
            {
                return "CODE";
            }

            return "TEXT";
        }

        private static bool IsCodeLike(string text)
        {
            if (text.Length < 6)
            {
                return false;
            }

            bool hasBraces = text.IndexOf('{') >= 0 && text.IndexOf('}') >= 0;
            bool hasSemicolons = text.IndexOf(';') >= 0;
            bool hasHtmlTags = (text.IndexOf('<') >= 0 && text.IndexOf('>') >= 0) &&
                               (text.IndexOf("</", StringComparison.Ordinal) >= 0 || text.IndexOf("/>", StringComparison.Ordinal) >= 0);

            if (hasHtmlTags)
            {
                return true;
            }

            if (hasBraces && (hasSemicolons || text.IndexOf('\n') >= 0))
            {
                return true;
            }

            string[] codeKeywords = new string[] {
                "public ", "private ", "protected ", "internal ", "class ", "namespace ",
                "function ", "def ", "import ", "using ", "return ", "var ", "let ", "const ",
                "if (", "while (", "for (", "foreach (", "switch (", "catch (",
                "SELECT ", "FROM ", "WHERE ", "INSERT INTO ", "UPDATE ", "DELETE FROM "
            };

            for (int i = 0; i < codeKeywords.Length; i++)
            {
                if (text.IndexOf(codeKeywords[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (text.IndexOf('\n') >= 0 || hasSemicolons || hasBraces || text.IndexOf('(') >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
