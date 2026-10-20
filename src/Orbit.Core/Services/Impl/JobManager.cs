namespace Orbit.Core.Services.Impl;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Core.Models;
using Orbit.Core.Services;

public class JobManager : IJobManager
{
    private readonly IEnumerable<IProcessingEngine> _engines;
    private readonly ConcurrentDictionary<string, JobInfo> _jobs = new();
    private readonly List<JobInfo> _history = new();
    private readonly object _lock = new();

    public event EventHandler<JobInfo>? JobProgressChanged;
    public event EventHandler<JobInfo>? JobCompleted;
    public event EventHandler<JobInfo>? JobFailed;

    public JobManager(IEnumerable<IProcessingEngine> engines)
    {
        _engines = engines ?? throw new ArgumentNullException(nameof(engines));
    }

    public Task<string> EnqueueAsync(OperationRequest request)
    {
        var jobId = Guid.NewGuid().ToString("N");
        var jobInfo = new JobInfo
        {
            JobId = jobId,
            OperationId = request.OperationId,
            Request = request,
            Status = GestureState.Processing
        };

        _jobs[jobId] = jobInfo;

        // Start processing in background task
        _ = Task.Run(async () =>
        {
            await ProcessJobAsync(jobInfo);
        });

        return Task.FromResult(jobId);
    }

    private async Task ProcessJobAsync(JobInfo job)
    {
        try
        {
            // Find engine by EngineId prefix or matching capabilities
            var engine = _engines.FirstOrDefault(e => 
                job.Request.OperationId.StartsWith(e.EngineId, StringComparison.OrdinalIgnoreCase)) 
                ?? _engines.FirstOrDefault(e => e.IsAvailable);

            if (engine == null)
            {
                throw new InvalidOperationException($"No suitable processing engine found for operation '{job.Request.OperationId}'.");
            }

            var progress = new Progress<OperationProgress>(p =>
            {
                var updated = job with { Progress = p };
                _jobs[job.JobId] = updated;
                JobProgressChanged?.Invoke(this, updated);
            });

            var result = await engine.ExecuteAsync(job.Request, progress, CancellationToken.None);

            var completedJob = job with
            {
                Status = result.Success ? GestureState.Complete : GestureState.Cancelled,
                Result = result,
                CompletedAt = DateTime.UtcNow
            };

            _jobs[job.JobId] = completedJob;

            lock (_lock)
            {
                _history.Insert(0, completedJob);
                if (_history.Count > 100) _history.RemoveAt(_history.Count - 1);
            }

            if (result.Success)
            {
                JobCompleted?.Invoke(this, completedJob);
            }
            else
            {
                JobFailed?.Invoke(this, completedJob);
            }
        }
        catch (Exception ex)
        {
            var failedJob = job with
            {
                Status = GestureState.Cancelled,
                Result = new OperationResult
                {
                    OperationId = job.Request.OperationId,
                    Success = false,
                    ErrorMessage = ex.Message
                },
                CompletedAt = DateTime.UtcNow
            };

            _jobs[job.JobId] = failedJob;
            JobFailed?.Invoke(this, failedJob);
        }
    }

    public Task CancelAsync(string jobId)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            var cancelled = job with { Status = GestureState.Cancelled, CompletedAt = DateTime.UtcNow };
            _jobs[jobId] = cancelled;
            JobFailed?.Invoke(this, cancelled);
        }

        return Task.CompletedTask;
    }

    public Task<JobInfo?> GetJobAsync(string jobId)
    {
        _jobs.TryGetValue(jobId, out var job);
        return Task.FromResult(job);
    }

    public Task<IReadOnlyList<JobInfo>> GetActiveJobsAsync()
    {
        var active = _jobs.Values.Where(j => j.Status == GestureState.Processing).ToList();
        return Task.FromResult<IReadOnlyList<JobInfo>>(active);
    }

    public Task<IReadOnlyList<JobInfo>> GetHistoryAsync(int limit = 50)
    {
        lock (_lock)
        {
            var list = _history.Take(limit).ToList();
            return Task.FromResult<IReadOnlyList<JobInfo>>(list);
        }
    }
}
