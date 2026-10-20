namespace Orbit.Core.Services;

using Orbit.Core.Models;

public record JobInfo
{
    public required string JobId { get; init; }
    public required string OperationId { get; init; }
    public required OperationRequest Request { get; init; }
    public GestureState Status { get; init; }
    public OperationProgress? Progress { get; init; }
    public OperationResult? Result { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; init; }
}

public interface IJobManager
{
    Task<string> EnqueueAsync(OperationRequest request);
    Task CancelAsync(string jobId);
    Task<JobInfo?> GetJobAsync(string jobId);
    Task<IReadOnlyList<JobInfo>> GetActiveJobsAsync();
    Task<IReadOnlyList<JobInfo>> GetHistoryAsync(int limit = 50);
    event EventHandler<JobInfo>? JobProgressChanged;
    event EventHandler<JobInfo>? JobCompleted;
    event EventHandler<JobInfo>? JobFailed;
}
