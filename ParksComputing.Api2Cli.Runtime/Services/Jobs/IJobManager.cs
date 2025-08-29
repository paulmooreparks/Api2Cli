using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;

namespace ParksComputing.Api2Cli.Runtime.Services.Jobs;

/// <summary>
/// Represents a queued unit of asynchronous work executed serially (Phase 1).
/// </summary>
public interface IJobManager {
    Job Enqueue(JobRequest request);
    IReadOnlyCollection<Job> Jobs { get; }
    bool TryGet(Guid id, out Job job);
    bool Cancel(Guid id);
    event EventHandler<Job>? JobQueued;
    event EventHandler<Job>? JobStarted;
    event EventHandler<Job>? JobCompleted;
    event EventHandler<(Job job, Exception ex)>? JobFailed;
}

public enum JobStatus { Queued, Running, Succeeded, Failed, Cancelled }

public record JobRequest(string Kind, string Name, Func<CancellationToken, Task<object?>> Work, IDictionary<string, object?>? Args = null);

public class Job {
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Kind { get; init; } = string.Empty; // e.g. script, request
    public string Name { get; init; } = string.Empty; // scriptName or requestName
    public DateTimeOffset QueuedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public object? Result { get; set; }
    public Exception? Error { get; set; }
    public IDictionary<string, object?>? Args { get; init; }
    public Func<CancellationToken, Task<object?>> Work { get; init; } = _ => Task.FromResult<object?>(null);
    internal CancellationTokenSource CancellationSource { get; } = new();
    internal bool CancelRequested { get; set; }
}

internal class JobManager : IJobManager, IDisposable {
    private readonly ConcurrentQueue<Job> _queue = new();
    private readonly Dictionary<Guid, Job> _jobs = new();
    private readonly object _gate = new();
    private bool _draining;
    private readonly CancellationTokenSource _cts = new();

    public event EventHandler<Job>? JobQueued;
    public event EventHandler<Job>? JobStarted;
    public event EventHandler<Job>? JobCompleted;
    public event EventHandler<(Job job, Exception ex)>? JobFailed;

    public IReadOnlyCollection<Job> Jobs => new ReadOnlyCollection<Job>(_jobs.Values.OrderByDescending(j => j.QueuedAt).ToList());

    public Job Enqueue(JobRequest request) {
        var job = new Job { Kind = request.Kind, Name = request.Name, Args = request.Args, Work = request.Work };
        lock (_gate) { _jobs[job.Id] = job; }
        _queue.Enqueue(job);
        JobQueued?.Invoke(this, job);
        _ = EnsureDrainingAsync();
        return job;
    }

    public bool Cancel(Guid id) {
        lock (_gate) {
            if (!_jobs.TryGetValue(id, out var job)) return false;
            if (job.Status is JobStatus.Succeeded or JobStatus.Failed or JobStatus.Cancelled) return false;
            job.CancelRequested = true;
            try { job.CancellationSource.Cancel(); } catch { }
            return true;
        }
    }

    private async Task EnsureDrainingAsync() {
        // Single active drain loop (Phase 1 serial execution)
        lock (_gate) {
            if (_draining) return;
            _draining = true;
        }
        try {
            while (!_cts.IsCancellationRequested) {
                if (!_queue.TryDequeue(out var next)) break;
                if (next.CancelRequested && next.Status == JobStatus.Queued) {
                    next.Status = JobStatus.Cancelled;
                    next.CompletedAt = DateTimeOffset.UtcNow;
                    continue;
                }
                await RunJobAsync(next);
            }
        }
        finally {
            lock (_gate) { _draining = false; }
        }
    }

    private async Task RunJobAsync(Job job) {
        if (job.CancelRequested) {
            job.Status = JobStatus.Cancelled;
            job.CompletedAt = DateTimeOffset.UtcNow;
            return;
        }
        job.StartedAt = DateTimeOffset.UtcNow;
        job.Status = JobStatus.Running;
        JobStarted?.Invoke(this, job);
        try {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, job.CancellationSource.Token);
            job.Result = await job.Work(linked.Token);
            if (job.CancelRequested || linked.IsCancellationRequested) {
                job.Status = JobStatus.Cancelled;
                job.CompletedAt = DateTimeOffset.UtcNow;
                return;
            }
            job.Status = JobStatus.Succeeded;
            job.CompletedAt = DateTimeOffset.UtcNow;
            JobCompleted?.Invoke(this, job);
        }
        catch (Exception ex) {
            if (job.CancelRequested) {
                job.Status = JobStatus.Cancelled;
                job.CompletedAt = DateTimeOffset.UtcNow;
                return;
            }
            job.Error = ex;
            job.Status = JobStatus.Failed;
            job.CompletedAt = DateTimeOffset.UtcNow;
            JobFailed?.Invoke(this, (job, ex));
        }
    }

    public bool TryGet(Guid id, out Job job) {
        lock (_gate) return _jobs.TryGetValue(id, out job!);
    }

    public void Dispose() => _cts.Cancel();
}

public static class JobManagerServiceCollectionExtensions {
    public static IServiceCollection AddJobManager(this IServiceCollection services) {
        if (!services.Any(sd => sd.ServiceType == typeof(IJobManager))) {
            services.AddSingleton<IJobManager, JobManager>();
        }
        return services;
    }
}
