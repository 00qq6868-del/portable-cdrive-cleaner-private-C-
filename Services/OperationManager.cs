using System.Collections.Concurrent;
using PortableCDriveCleaner.Models;

namespace PortableCDriveCleaner.Services;

public sealed class OperationManager
{
    private readonly SemaphoreSlim _concurrencyGate;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _resourceLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private readonly List<MutableJob> _jobs = [];

    public OperationManager(int maxConcurrentJobs)
    {
        _concurrencyGate = new SemaphoreSlim(Math.Clamp(maxConcurrentJobs, 1, 2));
    }

    public event EventHandler<OperationQueueState>? StateChanged;

    public OperationQueueState GetState()
    {
        lock (_sync)
        {
            return new OperationQueueState
            {
                Jobs = _jobs
                    .OrderByDescending(job => job.CreatedAtUtc)
                    .Select(job => job.ToSnapshot())
                    .ToList(),
                RunningCount = _jobs.Count(job => job.State == OperationJobState.Running),
                QueuedCount = _jobs.Count(job => job.State == OperationJobState.Queued),
                CompletedCount = _jobs.Count(job => job.State is OperationJobState.Succeeded or OperationJobState.Failed or OperationJobState.Canceled),
                WarningCompletedCount = _jobs.Count(job => job.State == OperationJobState.Succeeded && job.HasWarnings)
            };
        }
    }

    public bool HasActiveJob(OperationJobKind kind)
    {
        lock (_sync)
        {
            return _jobs.Any(job => job.Kind == kind && job.State is OperationJobState.Queued or OperationJobState.Running);
        }
    }

    public OperationJob Enqueue<TResult>(
        OperationJobKind kind,
        string title,
        IEnumerable<string>? resourceKeys,
        Func<IProgress<OperationProgress>, CancellationToken, TResult> work,
        Func<TResult, int>? warningCountSelector = null,
        Func<TResult, string>? warningSummarySelector = null,
        Action<TResult>? completed = null,
        Action<Exception>? failed = null)
    {
        var job = new MutableJob
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            Title = title,
            State = OperationJobState.Queued,
            CreatedAtUtc = DateTime.UtcNow,
            Phase = "等待开始",
            Message = "已加入后台任务队列",
            Percent = 0,
            IsIndeterminate = true,
            JobScope = string.Empty
        };

        lock (_sync)
        {
            _jobs.Insert(0, job);
            TrimCompletedJobs_NoLock();
        }

        PublishState();
        _ = RunAsync(job, NormalizeResourceKeys(resourceKeys), work, warningCountSelector, warningSummarySelector, completed, failed);
        return job.ToSnapshot();
    }

    private async Task RunAsync<TResult>(
        MutableJob job,
        IReadOnlyList<string> resourceKeys,
        Func<IProgress<OperationProgress>, CancellationToken, TResult> work,
        Func<TResult, int>? warningCountSelector,
        Func<TResult, string>? warningSummarySelector,
        Action<TResult>? completed,
        Action<Exception>? failed)
    {
        var heldLocks = new List<SemaphoreSlim>();
        using var cancellation = new CancellationTokenSource();
        var progress = new Progress<OperationProgress>(update => UpdateProgress(job, update));

        try
        {
            await _concurrencyGate.WaitAsync(cancellation.Token).ConfigureAwait(false);
            foreach (var resourceKey in resourceKeys)
            {
                var semaphore = _resourceLocks.GetOrAdd(resourceKey, _ => new SemaphoreSlim(1, 1));
                await semaphore.WaitAsync(cancellation.Token).ConfigureAwait(false);
                heldLocks.Add(semaphore);
            }

            MarkRunning(job);
            var result = await Task.Run(() => work(progress, cancellation.Token), cancellation.Token).ConfigureAwait(false);
            var warningCount = warningCountSelector?.Invoke(result) ?? 0;
            var warningSummary = warningSummarySelector?.Invoke(result) ?? string.Empty;
            MarkCompleted(job, OperationJobState.Succeeded, "已完成", warningCount, warningSummary);
            completed?.Invoke(result);
        }
        catch (OperationCanceledException)
        {
            MarkCompleted(job, OperationJobState.Canceled, "已取消");
        }
        catch (Exception ex)
        {
            MarkFailed(job, ex);
            failed?.Invoke(ex);
        }
        finally
        {
            for (var i = heldLocks.Count - 1; i >= 0; i--)
            {
                heldLocks[i].Release();
            }

            _concurrencyGate.Release();
            PublishState();
        }
    }

    private static IReadOnlyList<string> NormalizeResourceKeys(IEnumerable<string>? keys)
    {
        return (keys ?? [])
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void MarkRunning(MutableJob job)
    {
        lock (_sync)
        {
            job.State = OperationJobState.Running;
            job.StartedAtUtc = DateTime.UtcNow;
            job.Phase = "正在运行";
            job.Message = "任务已经开始";
            job.IsIndeterminate = true;
            job.Percent = Math.Max(job.Percent, 1);
            job.JobScope = string.IsNullOrWhiteSpace(job.JobScope) ? job.Title : job.JobScope;
        }

        PublishState();
    }

    private void UpdateProgress(MutableJob job, OperationProgress update)
    {
        lock (_sync)
        {
            job.Phase = string.IsNullOrWhiteSpace(update.Phase) ? job.Phase : update.Phase;
            job.Message = string.IsNullOrWhiteSpace(update.Message) ? job.Message : update.Message;
            job.Percent = Math.Clamp(update.Percent, 0, 100);
            job.IsIndeterminate = update.IsIndeterminate;
            job.ProcessedBytes = update.ProcessedBytes;
            job.TotalBytes = update.TotalBytes;
            job.JobScope = string.IsNullOrWhiteSpace(update.JobScope) ? job.JobScope : update.JobScope;
        }

        PublishState();
    }

    private void MarkCompleted(MutableJob job, OperationJobState state, string summary, int warningCount = 0, string warningSummary = "")
    {
        lock (_sync)
        {
            job.State = state;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.SummaryText = summary;
            job.Phase = "已完成";
            job.WarningCount = Math.Max(warningCount, 0);
            job.HasWarnings = job.WarningCount > 0;
            job.WarningSummary = warningSummary ?? string.Empty;
            job.Message = string.IsNullOrWhiteSpace(job.WarningSummary) ? summary : job.WarningSummary;
            job.Percent = 100;
            job.IsIndeterminate = false;
            TrimCompletedJobs_NoLock();
        }
    }

    private void MarkFailed(MutableJob job, Exception exception)
    {
        lock (_sync)
        {
            job.State = OperationJobState.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
            job.SummaryText = "执行失败";
            job.Phase = "失败";
            job.Message = string.IsNullOrWhiteSpace(exception.Message) ? "后台任务失败" : exception.Message;
            job.ErrorMessage = job.Message;
            job.IsIndeterminate = false;
            TrimCompletedJobs_NoLock();
        }
    }

    private void TrimCompletedJobs_NoLock()
    {
        var completed = _jobs
            .Where(job => job.State is OperationJobState.Succeeded or OperationJobState.Failed or OperationJobState.Canceled)
            .OrderByDescending(job => job.CompletedAtUtc)
            .Skip(6)
            .ToList();

        foreach (var job in completed)
        {
            _jobs.Remove(job);
        }
    }

    private void PublishState()
    {
        StateChanged?.Invoke(this, GetState());
    }

    private sealed class MutableJob
    {
        public Guid Id { get; init; }
        public OperationJobKind Kind { get; init; }
        public string Title { get; init; } = string.Empty;
        public OperationJobState State { get; set; }
        public string Phase { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public int Percent { get; set; }
        public bool IsIndeterminate { get; set; }
        public long? ProcessedBytes { get; set; }
        public long? TotalBytes { get; set; }
        public string JobScope { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; init; }
        public DateTime? StartedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public string SummaryText { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public bool HasWarnings { get; set; }
        public int WarningCount { get; set; }
        public string WarningSummary { get; set; } = string.Empty;

        public OperationJob ToSnapshot()
        {
            return new OperationJob
            {
                Id = Id,
                Kind = Kind,
                State = State,
                Title = Title,
                Phase = Phase,
                Message = Message,
                Percent = Percent,
                IsIndeterminate = IsIndeterminate,
                ProcessedBytes = ProcessedBytes,
                TotalBytes = TotalBytes,
                JobScope = JobScope,
                CreatedAtUtc = CreatedAtUtc,
                StartedAtUtc = StartedAtUtc,
                CompletedAtUtc = CompletedAtUtc,
                SummaryText = SummaryText,
                ErrorMessage = ErrorMessage,
                HasWarnings = HasWarnings,
                WarningCount = WarningCount,
                WarningSummary = WarningSummary
            };
        }
    }
}
