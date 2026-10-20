namespace Orbit.Core.Models;

public record OperationRequest
{
    public required string OperationId { get; init; }
    public required FileIdentity[] InputFiles { get; init; }
    public string? OutputDirectory { get; init; }
    public string? OutputFormat { get; init; }
    public Dictionary<string, object> Parameters { get; init; } = new();
}

public record OperationProgress
{
    public required string OperationId { get; init; }
    public double PercentComplete { get; init; }
    public string? StatusMessage { get; init; }
    public string? CurrentFile { get; init; }
    public int FilesProcessed { get; init; }
    public int TotalFiles { get; init; }
}

public record OperationResult
{
    public required string OperationId { get; init; }
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string[]? OutputFiles { get; init; }
    public TimeSpan Duration { get; init; }
    public Dictionary<string, object> ResultMetadata { get; init; } = new();
}
