using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UberDownloader.Core.Data;
using UberDownloader.Core.Models;

namespace UberDownloader.Core.Services;

public class DownloadScheduler
{
    private class QueueEntry
    {
        public DownloadItem Item { get; set; } = null!;
        public Action<DownloadItem> OnProgress { get; set; } = null!;
        public Action<DownloadItem> OnCompleted { get; set; } = null!;
        public CancellationTokenSource Cts { get; set; } = new();
    }

    private readonly DownloadService _downloadService;
    private readonly DatabaseService _databaseService;
    private readonly SettingsService _settingsService;

    private readonly List<QueueEntry> _queue = new();
    private readonly object _queueLock = new();

    private readonly ConcurrentDictionary<string, QueueEntry> _activeJobs = new();
    private int _currentMaxConcurrent;

    public DownloadScheduler(DownloadService downloadService, DatabaseService databaseService, SettingsService settingsService)
    {
        _downloadService = downloadService;
        _databaseService = databaseService;
        _settingsService = settingsService;

        int configuredMax = _settingsService.Settings.MaxConcurrentDownloads;
        _currentMaxConcurrent = (configuredMax <= 0 || configuredMax == int.MaxValue) ? int.MaxValue : Math.Max(1, configuredMax);
    }

    public void UpdateConcurrency(int newMax)
    {
        lock (_queueLock)
        {
            _currentMaxConcurrent = (newMax <= 0 || newMax == int.MaxValue) ? int.MaxValue : Math.Max(1, newMax);
        }

        ProcessQueue();
    }

    public void Enqueue(DownloadItem item, Action<DownloadItem> onProgress, Action<DownloadItem> onCompleted)
    {
        item.Status = JobStatus.Queued;
        item.StatusDetail = LocalizationService.Instance["StatusQueued"];

        var entry = new QueueEntry
        {
            Item = item,
            OnProgress = onProgress,
            OnCompleted = onCompleted,
            Cts = new CancellationTokenSource()
        };

        lock (_queueLock)
        {
            _queue.Add(entry);
        }

        _ = _databaseService.UpsertItemAsync(item);
        onProgress(item);

        ProcessQueue();
    }

    public void EnqueueRange(IEnumerable<DownloadItem> items, Action<DownloadItem> onProgress, Action<DownloadItem> onCompleted)
    {
        lock (_queueLock)
        {
            foreach (var item in items)
            {
                item.Status = JobStatus.Queued;
                item.StatusDetail = LocalizationService.Instance["StatusQueued"];

                var entry = new QueueEntry
                {
                    Item = item,
                    OnProgress = onProgress,
                    OnCompleted = onCompleted,
                    Cts = new CancellationTokenSource()
                };

                _queue.Add(entry);
                _ = _databaseService.UpsertItemAsync(item);
                onProgress(item);
            }
        }

        ProcessQueue();
    }

    public void Cancel(string itemId)
    {
        QueueEntry? active;
        if (_activeJobs.TryGetValue(itemId, out active))
        {
            try { active.Cts.Cancel(); } catch { }
            active.Item.Status = JobStatus.Cancelled;
            active.Item.StatusDetail = LocalizationService.Instance["StatusCancelled"];
            active.OnProgress(active.Item);
            _ = _databaseService.UpsertItemAsync(active.Item);
        }

        lock (_queueLock)
        {
            var pending = _queue.FirstOrDefault(e => e.Item.Id == itemId);
            if (pending != null)
            {
                _queue.Remove(pending);
                pending.Item.Status = JobStatus.Cancelled;
                pending.Item.StatusDetail = LocalizationService.Instance["StatusCancelled"];
                pending.OnProgress(pending.Item);
                _ = _databaseService.UpsertItemAsync(pending.Item);
            }
        }
    }

    public void Pause(string itemId)
    {
        QueueEntry? active;
        if (_activeJobs.TryGetValue(itemId, out active))
        {
            active.Item.Status = JobStatus.Paused;
            active.Item.StatusDetail = LocalizationService.Instance["StatusPaused"];
            try { active.Cts.Cancel(); } catch { }
            active.OnProgress(active.Item);
            _ = _databaseService.UpsertItemAsync(active.Item);
        }

        lock (_queueLock)
        {
            var pending = _queue.FirstOrDefault(e => e.Item.Id == itemId);
            if (pending != null)
            {
                _queue.Remove(pending);
                pending.Item.Status = JobStatus.Paused;
                pending.Item.StatusDetail = LocalizationService.Instance["StatusPaused"];
                pending.OnProgress(pending.Item);
                _ = _databaseService.UpsertItemAsync(pending.Item);
            }
        }
    }

    public void CancelPlaylist(string playlistId)
    {
        foreach (var kvp in _activeJobs)
        {
            if (kvp.Value.Item.PlaylistId == playlistId)
            {
                try { kvp.Value.Cts.Cancel(); } catch { }
                kvp.Value.Item.Status = JobStatus.Cancelled;
                kvp.Value.Item.StatusDetail = LocalizationService.Instance["StatusCancelled"];
                kvp.Value.OnProgress(kvp.Value.Item);
                _ = _databaseService.UpsertItemAsync(kvp.Value.Item);
            }
        }

        lock (_queueLock)
        {
            var toRemove = _queue.Where(e => e.Item.PlaylistId == playlistId).ToList();
            foreach (var entry in toRemove)
            {
                _queue.Remove(entry);
                entry.Item.Status = JobStatus.Cancelled;
                entry.Item.StatusDetail = LocalizationService.Instance["StatusCancelled"];
                entry.OnProgress(entry.Item);
                _ = _databaseService.UpsertItemAsync(entry.Item);
            }
        }
    }

    public void PausePlaylist(string playlistId)
    {
        foreach (var kvp in _activeJobs)
        {
            if (kvp.Value.Item.PlaylistId == playlistId)
            {
                kvp.Value.Item.Status = JobStatus.Paused;
                kvp.Value.Item.StatusDetail = LocalizationService.Instance["StatusPaused"];
                try { kvp.Value.Cts.Cancel(); } catch { }
                kvp.Value.OnProgress(kvp.Value.Item);
                _ = _databaseService.UpsertItemAsync(kvp.Value.Item);
            }
        }

        lock (_queueLock)
        {
            var toPause = _queue.Where(e => e.Item.PlaylistId == playlistId).ToList();
            foreach (var entry in toPause)
            {
                _queue.Remove(entry);
                entry.Item.Status = JobStatus.Paused;
                entry.Item.StatusDetail = LocalizationService.Instance["StatusPaused"];
                entry.OnProgress(entry.Item);
                _ = _databaseService.UpsertItemAsync(entry.Item);
            }
        }
    }

    public void PauseAll()
    {
        foreach (var kvp in _activeJobs)
        {
            kvp.Value.Item.Status = JobStatus.Paused;
            kvp.Value.Item.StatusDetail = LocalizationService.Instance["StatusPaused"];
            try { kvp.Value.Cts.Cancel(); } catch { }
            kvp.Value.OnProgress(kvp.Value.Item);
            _ = _databaseService.UpsertItemAsync(kvp.Value.Item);
        }

        lock (_queueLock)
        {
            foreach (var entry in _queue)
            {
                entry.Item.Status = JobStatus.Paused;
                entry.Item.StatusDetail = LocalizationService.Instance["StatusPaused"];
                entry.OnProgress(entry.Item);
                _ = _databaseService.UpsertItemAsync(entry.Item);
            }
            _queue.Clear();
        }
    }

    public void CancelAll()
    {
        foreach (var kvp in _activeJobs)
        {
            kvp.Value.Item.Status = JobStatus.Cancelled;
            kvp.Value.Item.StatusDetail = LocalizationService.Instance["StatusCancelled"];
            try { kvp.Value.Cts.Cancel(); } catch { }
            kvp.Value.OnProgress(kvp.Value.Item);
            _ = _databaseService.UpsertItemAsync(kvp.Value.Item);
        }

        lock (_queueLock)
        {
            foreach (var entry in _queue)
            {
                entry.Item.Status = JobStatus.Cancelled;
                entry.Item.StatusDetail = LocalizationService.Instance["StatusCancelled"];
                entry.OnProgress(entry.Item);
                _ = _databaseService.UpsertItemAsync(entry.Item);
            }
            _queue.Clear();
        }
    }

    public bool HasActiveDownloads => _activeJobs.Count > 0 || QueuedDownloadsCount > 0;
    public int ActiveDownloadsCount => _activeJobs.Count;
    public int QueuedDownloadsCount
    {
        get
        {
            lock (_queueLock)
            {
                return _queue.Count;
            }
        }
    }

    public bool IsActive(string itemId) => _activeJobs.ContainsKey(itemId);

    public bool TryGetActive(string itemId, out DownloadItem? item)
    {
        if (_activeJobs.TryGetValue(itemId, out var entry))
        {
            item = entry.Item;
            return true;
        }
        item = null;
        return false;
    }

    public bool HasPendingOrActive(string playlistId)
    {
        if (_activeJobs.Values.Any(e => e.Item.PlaylistId == playlistId)) return true;
        lock (_queueLock)
        {
            return _queue.Any(e => e.Item.PlaylistId == playlistId);
        }
    }

    private void ProcessQueue()
    {
        List<QueueEntry> toStart = new();

        lock (_queueLock)
        {
            while (_activeJobs.Count < _currentMaxConcurrent && _queue.Count > 0)
            {
                var nextEntry = _queue[0];
                _queue.RemoveAt(0);

                if (nextEntry.Cts.IsCancellationRequested || nextEntry.Item.Status == JobStatus.Cancelled || nextEntry.Item.Status == JobStatus.Paused)
                {
                    continue;
                }

                _activeJobs[nextEntry.Item.Id] = nextEntry;
                toStart.Add(nextEntry);
            }
        }

        foreach (var entry in toStart)
        {
            StartJob(entry);
        }
    }

    private void StartJob(QueueEntry entry)
    {
        _ = Task.Run(async () =>
        {
            var item = entry.Item;
            item.Status = JobStatus.Downloading;
            item.Attempts++;
            item.StatusDetail = LocalizationService.Instance["StatusStarting"];
            entry.OnProgress(item);
            await _databaseService.UpsertItemAsync(item);

            bool willRetry = false;
            try
            {
                await _downloadService.ExecuteDownloadAsync(item, entry.OnProgress, entry.Cts.Token);
            }
            catch (Exception ex)
            {
                if (entry.Cts.IsCancellationRequested || item.Status == JobStatus.Paused || item.Status == JobStatus.Cancelled)
                {
                    // Statut déjà fixé
                }
                else
                {
                    item.Status = JobStatus.Error;
                    item.LastError = ex.Message;
                    item.ErrorMessage = ex.Message;
                }
                entry.OnProgress(item);
            }

            if (item.Status == JobStatus.Error && !entry.Cts.IsCancellationRequested)
            {
                int cfgRetries = _settingsService.Settings.MaxRetries;
                bool isUnlimitedRetries = (cfgRetries < 0 || cfgRetries == int.MaxValue);
                int maxRetries = isUnlimitedRetries ? int.MaxValue : Math.Max(0, cfgRetries);
                var classification = ErrorClassifier.Classify(item.ErrorMessage, item.Attempts, maxRetries);

                if (!classification.IsPermanent && (isUnlimitedRetries || item.Attempts < maxRetries))
                {
                    willRetry = true;
                    if (classification.ReduceConcurrency && _currentMaxConcurrent > 1 && _currentMaxConcurrent != int.MaxValue)
                    {
                        UpdateConcurrency(1);
                    }

                    int delaySec = Math.Max(3, classification.RetryDelaySeconds);
                    item.Status = JobStatus.Queued;
                    item.StatusDetail = isUnlimitedRetries
                        ? string.Format(LocalizationService.Instance["StatusRetryingUnlimited"], item.Attempts, delaySec)
                        : string.Format(LocalizationService.Instance["StatusRetrying"], item.Attempts, maxRetries, delaySec);
                    entry.OnProgress(item);
                    await _databaseService.UpsertItemAsync(item);

                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(delaySec));
                        if (!entry.Cts.IsCancellationRequested && item.Status == JobStatus.Queued)
                        {
                            lock (_queueLock)
                            {
                                _queue.Insert(0, entry);
                            }
                            ProcessQueue();
                        }
                    });
                }
            }

            try
            {
                _activeJobs.TryRemove(item.Id, out _);
                await _databaseService.UpsertItemAsync(item);
                if (!willRetry)
                {
                    entry.OnCompleted(item);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] StartJob completion error: {ex.Message}");
            }
            finally
            {
                ProcessQueue();
            }
        });
    }
}
